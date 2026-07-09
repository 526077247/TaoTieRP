using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class DepthOfFieldEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Depth Of Field");

        static readonly int
            dofSourceID = Shader.PropertyToID("_DOFSource"),
            dofCoCTextureID = Shader.PropertyToID("_DOFCoCTexture"),
            dofParamsID = Shader.PropertyToID("_DOFParams"),
            dofTexelSizeID = Shader.PropertyToID("_DOFTexelSize");

        [HideInInspector] public Shader dofShader;

        static Material dofMaterial;

        [System.Serializable]
        public struct DOFSettings
        {
            public float focusDistance;
            public float focusRange;
            public float blurStrength;
        }

        [System.NonSerialized] public DOFSettings settings;

        public override string DisplayName => "Depth Of Field";

        public override string ShaderName => "Hidden/TaoTie RP/Depth Of Field";

        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (dofShader == null)
                dofShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<DepthOfFieldVolume>();
            if (vol == null) return source;
            settings = new DOFSettings
            {
                focusDistance = vol.focusDistance.value,
                focusRange = vol.focusRange.value,
                blurStrength = vol.blurStrength.value
            };
            if (!IsEnabled)
                return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (dofMaterial == null || dofMaterial.passCount < 3)
                return source;

            TextureHandle depthTex = textures.depthCopy.IsValid()
                ? textures.depthCopy
                : textures.depthAttachment;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            Vector4 dofParams = new(settings.focusDistance, settings.focusRange, 0f, 0f);
            Vector4 texelSize = new(
                1f / stack.BufferSize.x, 1f / stack.BufferSize.y,
                stack.BufferSize.x, stack.BufferSize.y);

            // Pass 0+1: CoC + Blur in a single render pass
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                "DoF CoC+Blur", out DOFBlurPass blurPass, sampler);

            blurPass.material = dofMaterial;
            blurPass.source = builder.ReadTexture(source);
            blurPass.depthTexture = builder.ReadTexture(depthTex);
            blurPass.camera = camera;
            blurPass.bufferSize = stack.BufferSize;
            blurPass.dofParams = dofParams;
            blurPass.texelSize = texelSize;
            blurPass.blurStrength = settings.blurStrength;

            var blurDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "DoF Blurred"
            };
            blurPass.blurResult = builder.WriteTexture(renderGraph.CreateTexture(blurDesc));

            var cocDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "DoF CoC"
            };
            blurPass.cocResult = builder.WriteTexture(renderGraph.CreateTexture(cocDesc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DOFBlurPass>(
                static (pass, context) => pass.Render(context));

            // Pass 2: Composite �?write to a NEW texture (not source) to avoid read-write hazard
            using RenderGraphBuilder builder2 = renderGraph.AddRenderPass(
                "DoF Composite", out DOFCompositePass compositePass, sampler);

            compositePass.material = dofMaterial;
            compositePass.source = builder2.ReadTexture(source);
            compositePass.blurredTexture = builder2.ReadTexture(blurPass.blurResult);
            compositePass.camera = camera;
            compositePass.bufferSize = stack.BufferSize;
            compositePass.texelSize = texelSize;

            var resultDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "DoF Result"
            };
            compositePass.resultTexture = builder2.WriteTexture(renderGraph.CreateTexture(resultDesc));

            builder2.AllowPassCulling(false);
            builder2.SetRenderFunc<DOFCompositePass>(
                static (pass, context) => pass.Render(context));

            return compositePass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = dofShader;
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
                dofMaterial = null;
                return;
            }
            if (dofMaterial == null || dofMaterial.shader != shader)
            {
                if (dofMaterial != null)
                    CoreUtils.Destroy(dofMaterial);
                dofMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (dofMaterial != null)
            {
                CoreUtils.Destroy(dofMaterial);
                dofMaterial = null;
            }
        }

        class DOFBlurPass
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle depthTexture;
            public Camera camera;
            public Vector2Int bufferSize;
            public Vector4 dofParams;
            public Vector4 texelSize;
            public float blurStrength;
            public TextureHandle blurResult;
            public TextureHandle cocResult;

            public void Render(RenderGraphContext context)
            {
                if (material == null || material.passCount < 3) return;

                CommandBuffer cmd = context.cmd;

                // Pass 0: CoC into cocResult
                cmd.SetGlobalTexture(dofSourceID, source);
                cmd.SetGlobalTexture("_CameraDepthTexture", depthTexture);
                cmd.SetGlobalVector(dofParamsID, dofParams);
                cmd.SetGlobalVector(dofTexelSizeID, texelSize);

                cmd.SetRenderTarget(cocResult,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, material, 0, 0);

                // Pass 1: Blur �?read from cocResult, write to blurResult
                cmd.SetGlobalTexture(dofSourceID, cocResult);
                cmd.SetGlobalVector(dofTexelSizeID, texelSize * blurStrength);

                cmd.SetRenderTarget(blurResult,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, material, 0, 1);

                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }

        class DOFCompositePass
        {
            public Material material;
            public TextureHandle source;
            public TextureHandle blurredTexture;
            public Camera camera;
            public Vector2Int bufferSize;
            public Vector4 texelSize;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                if (material == null || material.passCount < 3) return;

                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(dofSourceID, source);
                cmd.SetGlobalTexture(dofCoCTextureID, blurredTexture);
                cmd.SetGlobalVector(dofTexelSizeID, texelSize);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, material, 0, 2);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
