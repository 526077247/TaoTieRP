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

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;
            // Explicitly bind color + depth so skybox ZTest works against
            // geometry depth written by the G-Buffer / forward opaque pass.
            cmd.SetRenderTarget(
                colorAttachment,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthAttachment,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, camera.pixelWidth, camera.pixelHeight));
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
            context.renderContext.DrawSkybox(camera);
        }

        public static void Record(RenderGraph renderGraph, Camera camera,
            in CameraRendererTextures textures)
        {
            if (camera.clearFlags == CameraClearFlags.Skybox)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    sampler.name, out SkyboxPass pass, sampler);
                pass.camera = camera;
                pass.colorAttachment = textures.colorAttachment;
                pass.depthAttachment = textures.depthAttachment;

                builder.WriteTexture(textures.colorAttachment);
                builder.ReadTexture(textures.depthAttachment);

                builder.SetRenderFunc<SkyboxPass>(
                    static (pass, context) => pass.Render(context));
            }
        }
    }
}