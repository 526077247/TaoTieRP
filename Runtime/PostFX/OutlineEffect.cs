using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class OutlineEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("OutLine");

        static readonly int
            outlineSourceID = Shader.PropertyToID("_OutlineSource"),
            cameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"),
            gBufferNormalMSID = Shader.PropertyToID("_GBufferNormalMS"),
            outlineColorID = Shader.PropertyToID("_OutlineColor"),
            outlineDepthSensitivityID = Shader.PropertyToID("_OutlineDepthSensitivity"),
            outlineNormalSensitivityID = Shader.PropertyToID("_OutlineNormalSensitivity"),
            outlineWidthID = Shader.PropertyToID("_OutlineWidth");

        static Material outlineMaterial;

        [HideInInspector] public Shader outlineShader;
        static Shader cachedShader;
        static bool disposedRegistered;

        [System.Serializable]
        public struct OutlineSettings
        {
            public Color color;
            public float depthSensitivity;
            public float normalSensitivity;
            public float width;
        }

        [System.NonSerialized] public OutlineSettings settings;

        public override string DisplayName => "Outline";

        public override string ShaderName => "Hidden/TaoTie RP/Outline";

        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (outlineShader == null)
                outlineShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<OutlineVolume>();
            if (vol == null) return source;
            settings = new OutlineSettings
            {
                color = vol.color.value,
                depthSensitivity = vol.depthSensitivity.value,
                normalSensitivity = vol.normalSensitivity.value,
                width = vol.width.value
            };

            if (!IsEnabled || settings.color.a <= 0f || settings.width <= 0f)
                return source;

            // Skip for SceneView and Preview cameras (matches previous behavior)
            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out OutlineRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.colorSource = builder.ReadTexture(colorSource);
            // Use the same depth source as TAAResolvePass: depthCopy (non-MSAA) if available, else depthAttachment
            TextureHandle depthTex = textures.depthCopy.IsValid()
                ? textures.depthCopy
                : textures.depthAttachment;
            pass.depthTexture = builder.ReadTexture(depthTex);
            if (stack.UseGBufferNormals && stack.GBufferNormalMS.IsValid())
                pass.gBufferNormalMS = builder.ReadTexture(stack.GBufferNormalMS);
            else
                pass.gBufferNormalMS = default;

            pass.colorAttachment = builder.WriteTexture(colorSource);

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                msaaSamples = stack.MSAA,
                name = "Outline Temp Result"
            };
            pass.tempResult = builder.WriteTexture(renderGraph.CreateTexture(desc));

            pass.camera = camera;
            pass.bufferSize = stack.BufferSize;
            pass.outlineColor = settings.color;
            pass.outlineDepthSensitivity = settings.depthSensitivity;
            pass.outlineNormalSensitivity = settings.normalSensitivity;
            pass.outlineWidth = settings.width;
            pass.useGBufferNormals = stack.UseGBufferNormals && stack.GBufferNormalMS.IsValid();

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<OutlineRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.colorAttachment;
        }

        void EnsureMaterial()
        {
            Shader shader = outlineShader;
            if (shader == null)
            {
                if (cachedShader == null)
                    cachedShader = Shader.Find(ShaderName);
                shader = cachedShader;
            }
            else
            {
                cachedShader = shader;
            }
            if (shader == null)
            {
                outlineMaterial = null;
                return;
            }
            if (outlineMaterial == null || outlineMaterial.shader != shader)
            {
                if (outlineMaterial != null)
                    CoreUtils.Destroy(outlineMaterial);
                outlineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!disposedRegistered)
            {
                disposedRegistered = true;
                RegisterDispose(Dispose);
            }
        }

        protected override void DisposeInternal() => Dispose();

        static void Dispose()
        {
            if (outlineMaterial != null)
            {
                CoreUtils.Destroy(outlineMaterial);
                outlineMaterial = null;
            }
        }

        class OutlineRenderPass
        {
            public TextureHandle colorSource;
            public TextureHandle depthTexture;
            public TextureHandle gBufferNormalMS;
            public TextureHandle tempResult;
            public TextureHandle colorAttachment;
            public Camera camera;
            public Vector2Int bufferSize;
            public Color outlineColor;
            public float outlineDepthSensitivity;
            public float outlineNormalSensitivity;
            public float outlineWidth;
            public bool useGBufferNormals;

            public void Render(RenderGraphContext context)
            {
                if (outlineMaterial == null || outlineMaterial.passCount < 2) return;

                CommandBuffer cmd = context.cmd;

                if (useGBufferNormals)
                    outlineMaterial.EnableKeyword("_OUTLINE_USE_GBUFFER_NORMALS");
                else
                    outlineMaterial.DisableKeyword("_OUTLINE_USE_GBUFFER_NORMALS");

                cmd.SetGlobalTexture(outlineSourceID, colorSource);
                cmd.SetGlobalTexture(cameraDepthTextureID, depthTexture);
                if (useGBufferNormals && gBufferNormalMS.IsValid())
                    cmd.SetGlobalTexture(gBufferNormalMSID, gBufferNormalMS);

                cmd.SetGlobalVector(outlineColorID, outlineColor);
                cmd.SetGlobalFloat(outlineDepthSensitivityID, outlineDepthSensitivity);
                cmd.SetGlobalFloat(outlineNormalSensitivityID, outlineNormalSensitivity);
                cmd.SetGlobalFloat(outlineWidthID, outlineWidth);

                // Pass 0: detect edges into temp
                cmd.SetRenderTarget(
                    tempResult,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, outlineMaterial, 0, 0);

                // Pass 1: composite back to color attachment
                cmd.SetGlobalTexture(outlineSourceID, tempResult);
                cmd.SetRenderTarget(
                    colorAttachment,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, outlineMaterial, 0, 1);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
