using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class VignetteEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Vignette");
        static readonly int
            vignetteSourceID = Shader.PropertyToID("_VignetteSource"),
            vignetteIntensityID = Shader.PropertyToID("_VignetteIntensity"),
            vignetteSmoothnessID = Shader.PropertyToID("_VignetteSmoothness"),
            vignetteCenterID = Shader.PropertyToID("_VignetteCenter"),
            vignetteRoundnessID = Shader.PropertyToID("_VignetteRoundness"),
            vignetteColorID = Shader.PropertyToID("_VignetteColor");

        static Material vignetteMaterial;

        [HideInInspector] public Shader vignetteShader;

        [System.Serializable]
        public struct VignetteSettings
        {
            public float intensity;
            public float smoothness;
            public Vector2 center;
            public float roundness;
            [ColorUsage(false)] public Color color;
        }

        [System.NonSerialized] public VignetteSettings settings;
        public override string DisplayName => "Vignette";
        public override string ShaderName => "Hidden/TaoTie RP/Vignette";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (vignetteShader == null)
                vignetteShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<VignetteVolume>();
            if (vol == null) return source;
            settings = new VignetteSettings
            {
                intensity = vol.intensity.value,
                smoothness = vol.smoothness.value,
                center = vol.center.value,
                roundness = vol.roundness.value,
                color = vol.color.value
            };
            if (!IsEnabled) return source;

            EnsureMaterial();
            if (vignetteMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out VignetteRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.intensity = settings.intensity;
            pass.smoothness = settings.smoothness;
            pass.center = settings.center;
            pass.roundness = settings.roundness;
            pass.color = settings.color;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Vignette Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<VignetteRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = vignetteShader;
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
                vignetteMaterial = null;
                return;
            }
            if (vignetteMaterial == null || vignetteMaterial.shader != shader)
            {
                if (vignetteMaterial != null) CoreUtils.Destroy(vignetteMaterial);
                vignetteMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (vignetteMaterial != null)
            {
                CoreUtils.Destroy(vignetteMaterial);
                vignetteMaterial = null;
            }
        }

        class VignetteRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float intensity;
            public float smoothness;
            public Vector2 center;
            public float roundness;
            public Color color;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(vignetteSourceID, source);
                cmd.SetGlobalFloat(vignetteIntensityID, intensity);
                cmd.SetGlobalFloat(vignetteSmoothnessID, smoothness);
                cmd.SetGlobalVector(vignetteCenterID, center);
                cmd.SetGlobalFloat(vignetteRoundnessID, roundness);
                cmd.SetGlobalColor(vignetteColorID, color);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, vignetteMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
