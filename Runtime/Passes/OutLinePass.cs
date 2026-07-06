using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
	public class OutLinePass
	{
		private static readonly ProfilingSampler
			samplerOutLine = new("OutLine");

		static readonly ShaderTagId[] shaderTagIDs =
		{
			new("Outline"),
		};

		RendererListHandle list;

		void Render(RenderGraphContext context)
		{
			context.cmd.DrawRendererList(list);
			context.renderContext.ExecuteCommandBuffer(context.cmd);
			context.cmd.Clear();
		}

		public static void Record(
			RenderGraph renderGraph,
			Camera camera,
			CullingResults cullingResults,
			int renderingLayerMask,
			in CameraRendererTextures textures,
			in ShadowTextures shadowTextures)
		{
			ProfilingSampler sampler = samplerOutLine;

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out OutLinePass pass, sampler);

			pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
				new RendererListDesc(shaderTagIDs, cullingResults, camera)
				{
					sortingCriteria = SortingCriteria.CommonOpaque,
					rendererConfiguration =
						PerObjectData.ReflectionProbes |
						PerObjectData.Lightmaps |
						PerObjectData.ShadowMask |
						PerObjectData.LightProbe |
						PerObjectData.OcclusionProbe |
						PerObjectData.LightProbeProxyVolume |
						PerObjectData.OcclusionProbeProxyVolume,
					renderQueueRange = RenderQueueRange.opaque,
					renderingLayerMask = (uint) renderingLayerMask
				}));

			builder.ReadWriteTexture(textures.colorAttachment);
			builder.ReadWriteTexture(textures.depthAttachment);

			builder.ReadTexture(shadowTextures.directionalAtlas);
			builder.ReadTexture(shadowTextures.otherAtlas);

			builder.SetRenderFunc<OutLinePass>(
				static (pass, context) => pass.Render(context));
		}
	}
}