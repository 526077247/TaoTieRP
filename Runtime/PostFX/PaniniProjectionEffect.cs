using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class PaniniProjectionEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Panini Projection");
        static readonly int
            ppSourceID = Shader.PropertyToID("_PPSource"),
            ppDistanceID = Shader.PropertyToID("_PPDistance"),
            ppCropToFitID = Shader.PropertyToID("_PPCropToFit");

        static Material ppMaterial;
        static Shader cachedShader;
        static bool disposedRegistered;

        [HideInInspector] public Shader ppShader;

        [System.Serializable]
        public struct PPSettings
        {
            public float distance;
            public float cropToFit;
        }

        [System.NonSerialized] public PPSettings settings;
        public override string DisplayName => "Panini Projection";


        public override string ShaderName => "Hidden/TaoTie RP/Panini Projection";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (ppShader == null)
                ppShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<PaniniProjectionVolume>();
            if (vol == null) return source;
            settings = new PPSettings
            {
                distance = vol.distance.value,
                cropToFit = vol.cropToFit.value
            };

            if (!IsEnabled || settings.distance <= 0f) return source;

            EnsureMaterial();
            if (ppMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out PPRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = stack.Camera;
            pass.bufferSize = stack.BufferSize;
            pass.distance = settings.distance;
            pass.cropToFit = settings.cropToFit;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Panini Projection Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<PPRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        void EnsureMaterial()
        {
            Shader shader = ppShader;
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
            if (ppMaterial == null || ppMaterial.shader != shader)
            {
                if (shader == null) { ppMaterial = null; return; }
                if (ppMaterial != null) CoreUtils.Destroy(ppMaterial);
                ppMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (ppMaterial != null)
            {
                CoreUtils.Destroy(ppMaterial);
                ppMaterial = null;
            }
        }

        class PPRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public float distance;
            public float cropToFit;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(ppSourceID, source);
                cmd.SetGlobalFloat(ppDistanceID, distance);
                cmd.SetGlobalFloat(ppCropToFitID, cropToFit);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ppMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
