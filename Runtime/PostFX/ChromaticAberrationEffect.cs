using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class ChromaticAberrationEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Chromatic Aberration");
        static readonly int
            caSourceID = Shader.PropertyToID("_CASource"),
            caIntensityID = Shader.PropertyToID("_CAIntensity"),
            caCenterID = Shader.PropertyToID("_CACenter");

        static Material caMaterial;

        [HideInInspector] public Shader caShader;

        [System.Serializable]
        public struct CASettings
        {
            public float intensity;
            public Vector2 center;
        }

        [System.NonSerialized] public CASettings settings;

        public override string DisplayName => "Chromatic Aberration";
        public override string ShaderName => "Hidden/TaoTie RP/Chromatic Aberration";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (caShader == null)
                caShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<ChromaticAberrationVolume>();
            if (vol == null) return source;
            settings = new CASettings
            {
                intensity = vol.intensity.value,
                center = vol.center.value
            };

            if (!IsEnabled || settings.intensity <= 0f) return source;

            EnsureMaterial();
            if (caMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out CARenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.intensity = settings.intensity;
            pass.center = settings.center;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Chromatic Aberration Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<CARenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = caShader;
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
                caMaterial = null;
                return;
            }
            if (caMaterial == null || caMaterial.shader != shader)
            {
                if (caMaterial != null) CoreUtils.Destroy(caMaterial);
                caMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (caMaterial != null)
            {
                CoreUtils.Destroy(caMaterial);
                caMaterial = null;
            }
        }

        class CARenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float intensity;
            public Vector2 center;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(caSourceID, source);
                cmd.SetGlobalFloat(caIntensityID, intensity);
                cmd.SetGlobalVector(caCenterID, center);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, caMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
