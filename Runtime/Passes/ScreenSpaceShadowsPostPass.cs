using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class ScreenSpaceShadowsPostPass
    {
        static readonly ProfilingSampler sampler = new("Screen Space Shadows Post");

        static readonly GlobalKeyword sssKeyword =
            GlobalKeyword.Create("_SCREEN_SPACE_SHADOWS");

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;
            cmd.SetKeyword(sssKeyword, false);
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(RenderGraph renderGraph)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out ScreenSpaceShadowsPostPass pass, sampler);
            builder.AllowPassCulling(false);
            builder.SetRenderFunc<ScreenSpaceShadowsPostPass>(
                static (pass, context) => pass.Render(context));
        }
    }
}
