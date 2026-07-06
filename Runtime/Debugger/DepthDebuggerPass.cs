using System.Diagnostics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class DepthDebuggerPass
    {
        static readonly ProfilingSampler sampler = new("Depth Debugger");

        public TextureHandle depthTexture;

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph, in CameraRendererTextures textures, bool useDepthTexture)
        {
            if (!DepthDebugger.IsActive || !useDepthTexture) return;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out DepthDebuggerPass pass, sampler);

            pass.depthTexture = DepthDebugger.UseDepthCopy
                ? builder.ReadTexture(textures.depthCopy)
                : builder.ReadTexture(textures.depthAttachment);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DepthDebuggerPass>(
                static (pass, context) => DepthDebugger.Render(context, pass.depthTexture));
        }
    }
}
