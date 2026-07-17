using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class SkyboxPass
    {
        static readonly ProfilingSampler sampler = new("Skybox");

        Camera camera;
        TextureHandle colorAttachment, depthAttachment;
        Vector2Int bufferSize;

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;
            cmd.SetRenderTarget(
                colorAttachment,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthAttachment,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.renderContext.DrawSkybox(camera);
        }

        public static void Record(RenderGraph renderGraph, Camera camera,
            in CameraRendererTextures textures, Vector2Int bufferSize)
        {
            if (camera.clearFlags == CameraClearFlags.Skybox)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    sampler.name, out SkyboxPass pass, sampler);
                pass.camera = camera;
                pass.colorAttachment = textures.colorAttachment;
                pass.depthAttachment = textures.depthAttachment;
                pass.bufferSize = bufferSize;

                builder.WriteTexture(textures.colorAttachment);
                builder.ReadTexture(textures.depthAttachment);

                builder.SetRenderFunc<SkyboxPass>(
                    static (pass, context) => pass.Render(context));
            }
        }
    }
}