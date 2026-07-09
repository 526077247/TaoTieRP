using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class PosterizeEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Posterize");
        static readonly int
            posterizeSourceID = Shader.PropertyToID("_PosterizeSource"),
            posterizeLevelsID = Shader.PropertyToID("_PosterizeLevels");

        static Material posterizeMaterial;

        [HideInInspector] public Shader posterizeShader;

        [System.Serializable]
        public struct PosterizeSettings
        {
            public int levels;
        }

        [System.NonSerialized] public PosterizeSettings settings;
        public override string DisplayName => "Posterize";

        public override string ShaderName => "Hidden/TaoTie RP/Posterize";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (posterizeShader == null)
                posterizeShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<PosterizeVolume>();
            if (vol == null) return source;
            settings = new PosterizeSettings
            {
                levels = vol.levels.value
            };
            if (!IsEnabled) return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (posterizeMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out PosterizeRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.levels = settings.levels;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Posterize Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PosterizeRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = posterizeShader;
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
            if (posterizeMaterial == null || posterizeMaterial.shader != shader)
            {
                if (shader == null)
                {
                    posterizeMaterial = null;
                    return;
                }
                if (posterizeMaterial != null) CoreUtils.Destroy(posterizeMaterial);
                posterizeMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (posterizeMaterial != null)
            {
                CoreUtils.Destroy(posterizeMaterial);
                posterizeMaterial = null;
            }
        }

        class PosterizeRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float levels;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(posterizeSourceID, source);
                cmd.SetGlobalFloat(posterizeLevelsID, levels);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, posterizeMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
