using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class PixelateEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Pixelate");
        static readonly int
            pixelateSourceID = Shader.PropertyToID("_PixelateSource"),
            pixelateCellSizeID = Shader.PropertyToID("_PixelateCellSize"),
            pixelateTexelSizeID = Shader.PropertyToID("_PixelateTexelSize");

        static Material pixelateMaterial;

        [HideInInspector] public Shader pixelateShader;

        [System.Serializable]
        public struct PixelateSettings
        {
            public int cellSize;
        }

        [System.NonSerialized] public PixelateSettings settings;
        public override string DisplayName => "Pixelate";
        public override string ShaderName => "Hidden/TaoTie RP/Pixelate";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (pixelateShader == null)
                pixelateShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<PixelateVolume>();
            if (vol == null) return source;
            settings = new PixelateSettings
            {
                cellSize = vol.cellSize.value
            };
            if (!IsEnabled) return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (pixelateMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out PixelateRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.cellSize = settings.cellSize;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Pixelate Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PixelateRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = pixelateShader;
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
            if (pixelateMaterial == null || pixelateMaterial.shader != shader)
            {
                if (shader == null)
                {
                    pixelateMaterial = null;
                    return;
                }
                if (pixelateMaterial != null) CoreUtils.Destroy(pixelateMaterial);
                pixelateMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (pixelateMaterial != null)
            {
                CoreUtils.Destroy(pixelateMaterial);
                pixelateMaterial = null;
            }
        }

        class PixelateRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float cellSize;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(pixelateSourceID, source);
                cmd.SetGlobalFloat(pixelateCellSizeID, cellSize);
                cmd.SetGlobalVector(pixelateTexelSizeID, new Vector4(
                    1f / bufferSize.x, 1f / bufferSize.y, bufferSize.x, bufferSize.y));

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, pixelateMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
