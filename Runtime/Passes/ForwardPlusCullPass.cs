using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class ForwardPlusCullPass
    {
        static readonly ProfilingSampler sampler = new("Forward+ Cull");
        static readonly int cameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");

        TextureHandle depthTexture;
        bool useDepth25D;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            if (useDepth25D)
            {
                buffer.SetGlobalTexture(cameraDepthTextureID, depthTexture);
            }
            LightingPass.RenderForwardPlusCull(buffer);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            in CameraRendererTextures textures,
            bool useDepth25D)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out ForwardPlusCullPass pass, sampler);

            pass.useDepth25D = useDepth25D;

            // Only declare read dependency on depth when 2.5D culling is active
            if (useDepth25D)
                pass.depthTexture = builder.ReadTexture(textures.depthCopy);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<ForwardPlusCullPass>(
                static (pass, context) => pass.Render(context));
        }
    }
}
