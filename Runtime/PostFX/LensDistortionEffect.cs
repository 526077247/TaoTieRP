using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class LensDistortionEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Lens Distortion");
        static readonly int
            ldSourceID = Shader.PropertyToID("_LDSource"),
            ldIntensityID = Shader.PropertyToID("_LDIntensity"),
            ldCenterID = Shader.PropertyToID("_LDCenter"),
            ldScaleID = Shader.PropertyToID("_LDScale");

        static Material ldMaterial;

        [HideInInspector] public Shader ldShader;

        [System.Serializable]
        public struct LDSettings
        {
            public float intensity;
            public Vector2 center;
            public float scale;
        }

        [System.NonSerialized] public LDSettings settings;
        public override string DisplayName => "Lens Distortion";

        public override string ShaderName => "Hidden/TaoTie RP/Lens Distortion";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (ldShader == null)
                ldShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<LensDistortionVolume>();
            if (vol == null) return source;
            settings = new LDSettings
            {
                intensity = vol.intensity.value,
                center = vol.center.value,
                scale = vol.scale.value
            };

            if (!IsEnabled || Mathf.Abs(settings.intensity) < 0.001f) return source;

            EnsureMaterial();
            if (ldMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out LDRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.intensity = settings.intensity;
            pass.center = settings.center;
            pass.scale = Mathf.Max(settings.scale, 0.001f);

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Lens Distortion Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<LDRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = ldShader;
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
                ldMaterial = null;
                return;
            }
            if (ldMaterial == null || ldMaterial.shader != shader)
            {
                if (ldMaterial != null) CoreUtils.Destroy(ldMaterial);
                ldMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (ldMaterial != null)
            {
                CoreUtils.Destroy(ldMaterial);
                ldMaterial = null;
            }
        }

        class LDRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float intensity;
            public Vector2 center;
            public float scale;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(ldSourceID, source);
                cmd.SetGlobalFloat(ldIntensityID, intensity);
                cmd.SetGlobalVector(ldCenterID, center);
                cmd.SetGlobalFloat(ldScaleID, scale);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ldMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
