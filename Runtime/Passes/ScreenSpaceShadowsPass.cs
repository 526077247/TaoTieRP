using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class ScreenSpaceShadowsPass
    {
        static readonly ProfilingSampler sampler = new("Screen Space Shadows");

        static Material material;
        static Shader cachedShader;

        static readonly int
            screenSpaceShadowmapID = Shader.PropertyToID("_ScreenSpaceShadowmapTexture"),
            cameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"),
            inverseViewProjID = Shader.PropertyToID("_InverseViewProj");

        static readonly GlobalKeyword sssKeyword =
            GlobalKeyword.Create("_SCREEN_SPACE_SHADOWS");

        TextureHandle depthTexture;
        TextureHandle shadowmapTexture;
        TextureHandle rtColor;
        TextureHandle rtDepth;
        Camera camera;
        Vector2Int bufferSize;

        static void EnsureMaterial()
        {
            if (cachedShader == null)
                cachedShader = Shader.Find("Hidden/TaoTie RP/Screen Space Shadows");
            if (cachedShader == null)
            {
                material = null;
                return;
            }
            if (material == null || material.shader != cachedShader)
            {
                if (material != null) Object.DestroyImmediate(material);
                material = new Material(cachedShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        void Render(RenderGraphContext context)
        {
            EnsureMaterial();
            if (material == null) return;

            CommandBuffer cmd = context.cmd;

            // Set depth texture for the blit shader
            cmd.SetGlobalTexture(cameraDepthTextureID, depthTexture);

            // Set inverse view-projection for world position reconstruction (read at render time for correct jitter)
            Matrix4x4 view = camera.worldToCameraMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 invViewProj = (gpuProj * view).inverse;
            cmd.SetGlobalMatrix(inverseViewProjID, invViewProj);

            // Render to the shadowmap texture
            cmd.SetRenderTarget(shadowmapTexture,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, material, 0, 0);

            // Set global texture for opaque geometry
            cmd.SetGlobalTexture(screenSpaceShadowmapID, shadowmapTexture);
            cmd.SetKeyword(sssKeyword, true);

            // Restore render target
            cmd.SetRenderTarget(
                rtColor,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                rtDepth,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            cmd.SetViewProjectionMatrices(view, camera.projectionMatrix);

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            in CameraRendererTextures textures,
            Vector2Int bufferSize,
            Camera camera)
        {
            EnsureMaterial();
            if (material == null) return;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out ScreenSpaceShadowsPass pass, sampler);

            // Read depth (depthCopy if available, else depthAttachment)
            TextureHandle depthHandle = textures.depthCopy.IsValid()
                ? textures.depthCopy : textures.depthAttachment;
            pass.depthTexture = builder.ReadTexture(depthHandle);

            // Create R8 shadowmap texture
            var format = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Render)
                ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;
            var desc = new TextureDesc(bufferSize.x, bufferSize.y)
            {
                colorFormat = format,
                name = "Screen Space Shadowmap"
            };
            pass.shadowmapTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            // Keep color/depth for restore
            pass.rtColor = builder.UseColorBuffer(textures.colorAttachment, 0);
            pass.rtDepth = builder.ReadTexture(textures.depthAttachment);

            pass.bufferSize = bufferSize;
            pass.camera = camera;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<ScreenSpaceShadowsPass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Dispose()
        {
            if (material != null)
            {
                Object.DestroyImmediate(material);
                material = null;
            }
        }
    }
}
