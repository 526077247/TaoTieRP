using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
    public class DepthPrePass
    {
        static readonly ProfilingSampler sampler = new("Depth Pre-Pass");

        static readonly ShaderTagId[] shaderTagIDs = {
            new("DepthOnly")
        };

        static readonly int depthCopyID =
            Shader.PropertyToID("_CameraDepthTexture");

        RendererListHandle list;
        TextureHandle depthCopy;
        TextureHandle rtColor, rtDepth;

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;
            cmd.SetRenderTarget(depthCopy);
            cmd.ClearRenderTarget(true, false, Color.clear);
            cmd.DrawRendererList(list);
            cmd.SetGlobalTexture(depthCopyID, depthCopy);

            cmd.SetRenderTarget(
                rtColor,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                rtDepth,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
            );

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            Camera camera,
            CullingResults cullingResults,
            int renderingLayerMask,
            in CameraRendererTextures textures)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out DepthPrePass pass, sampler);

            pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
                new RendererListDesc(shaderTagIDs, cullingResults, camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque,
                    renderingLayerMask = (uint)renderingLayerMask
                }));

            pass.depthCopy = builder.WriteTexture(textures.depthCopy);
            pass.rtColor = textures.colorAttachment;
            pass.rtDepth = textures.depthAttachment;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DepthPrePass>(
                static (pass, context) => pass.Render(context));
        }
    }
}
