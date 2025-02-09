using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
    public class UnsupportedShadersPass
    {
#if UNITY_EDITOR
        static readonly ProfilingSampler sampler = new("Unsupported Shaders");
        
        static ShaderTagId[] shaderTagIds =
        {
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
            new ShaderTagId("PrepassBase"),
            new ShaderTagId("Vertex"),
            new ShaderTagId("VertexLMRGBM"),
            new ShaderTagId("VertexLM")
        };

        static Material errorMaterial;

        RendererListHandle list;

        void Render(RenderGraphContext context)
        {
            context.cmd.DrawRendererList(list);
            context.renderContext.ExecuteCommandBuffer(context.cmd);
            context.cmd.Clear();
        }
#endif

        [Conditional("UNITY_EDITOR")]
        public static void Record(RenderGraph renderGraph, Camera camera, CullingResults cullingResults)
        {
#if UNITY_EDITOR
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out UnsupportedShadersPass pass, sampler);
            if (errorMaterial == null)
            {
                errorMaterial = new(Shader.Find("Hidden/InternalErrorShader"));
            }

            pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
                new RendererListDesc(shaderTagIds, cullingResults, camera)
                {
                    overrideMaterial = errorMaterial,
                    renderQueueRange = RenderQueueRange.all
                }));

            builder.SetRenderFunc<UnsupportedShadersPass>(
                (pass, context) => pass.Render(context));
#endif
        }
    }
}