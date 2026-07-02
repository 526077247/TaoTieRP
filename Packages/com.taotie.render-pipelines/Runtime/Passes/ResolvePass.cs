using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class ResolvePass
    {
        static readonly ProfilingSampler sampler = new("Resolve MSAA");

        TextureHandle msaaColor, resolvedColor;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.CopyTexture(msaaColor, resolvedColor);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(RenderGraph renderGraph, in CameraRendererTextures textures)
        {
            using RenderGraphBuilder builder =
                renderGraph.AddRenderPass(sampler.name, out ResolvePass pass, sampler);
            pass.msaaColor = builder.ReadTexture(textures.colorAttachment);
            pass.resolvedColor = builder.WriteTexture(textures.resolvedColorAttachment);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<ResolvePass>(
                static (pass, context) => pass.Render(context));
        }
    }
}
