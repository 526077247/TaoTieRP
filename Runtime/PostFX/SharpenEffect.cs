using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class SharpenEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Sharpen");
        static readonly int
            sharpenSourceID = Shader.PropertyToID("_SharpenSource"),
            sharpenIntensityID = Shader.PropertyToID("_SharpenIntensity"),
            sharpenRadiusID = Shader.PropertyToID("_SharpenRadius"),
            sharpenTexelSizeID = Shader.PropertyToID("_SharpenTexelSize");

        static Material sharpenMaterial;

        [HideInInspector] public Shader sharpenShader;

        [System.Serializable]
        public struct SharpenSettings
        {
            public float intensity;
            public float radius;
        }

        [System.NonSerialized] public SharpenSettings settings;

        public override string DisplayName => "Sharpen";

        public override string ShaderName => "Hidden/TaoTie RP/Sharpen";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (sharpenShader == null)
                sharpenShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<SharpenVolume>();
            if (vol == null) return source;
            settings = new SharpenSettings
            {
                intensity = vol.intensity.value,
                radius = vol.radius.value
            };

            if (!IsEnabled || settings.intensity <= 0f) return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (sharpenMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out SharpenRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.intensity = settings.intensity;
            pass.radius = settings.radius;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Sharpen Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<SharpenRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = sharpenShader;
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
            if (sharpenMaterial == null || sharpenMaterial.shader != shader)
            {
                if (shader == null)
                {
                    sharpenMaterial = null;
                    return;
                }
                if (sharpenMaterial != null) CoreUtils.Destroy(sharpenMaterial);
                sharpenMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (sharpenMaterial != null)
            {
                CoreUtils.Destroy(sharpenMaterial);
                sharpenMaterial = null;
            }
        }

        class SharpenRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float intensity;
            public float radius;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(sharpenSourceID, source);
                cmd.SetGlobalFloat(sharpenIntensityID, intensity);
                cmd.SetGlobalFloat(sharpenRadiusID, radius);
                cmd.SetGlobalVector(sharpenTexelSizeID, new Vector4(
                    1f / bufferSize.x, 1f / bufferSize.y, bufferSize.x, bufferSize.y));

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, sharpenMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
