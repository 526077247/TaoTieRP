using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie
{
    public class PostFXPass
    {
        PostFXStack postFXStack;
        TextureHandle colorAttachment;
        
        void Render(RenderGraphContext context) =>
            postFXStack.Render(context, colorAttachment);

        public static void Record(RenderGraph renderGraph, PostFXStack postFXStack,
            in CameraRendererTextures textures)
        {
            using RenderGraphBuilder builder =
                renderGraph.AddRenderPass("Post FX", out PostFXPass pass);
            pass.postFXStack = postFXStack;
            pass.colorAttachment = builder.ReadTexture(textures.colorAttachment);
            builder.SetRenderFunc<PostFXPass>((pass, context) => pass.Render(context));
        }
    }
}
