using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
	public class GeometryPass
	{
		static readonly ProfilingSampler
			samplerOpaque = new("Opaque Geometry"),
			samplerTransparent = new("Transparent Geometry");

		static readonly ShaderTagId[] shaderTagIDs =
		{
			new("SRPDefaultUnlit"),
			new("CustomLit")
		};

		RendererListHandle list;
		TextureHandle colorAttachment, depthAttachment;
		bool needRenderTargetSetup;

		void Render(RenderGraphContext context)
		{
			if (needRenderTargetSetup)
			{
				context.cmd.SetRenderTarget(
					colorAttachment,
					RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
					depthAttachment,
					RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
			}
			context.cmd.DrawRendererList(list);
			context.renderContext.ExecuteCommandBuffer(context.cmd);
			context.cmd.Clear();
		}

		public static void Record(
			RenderGraph renderGraph,
			Camera camera,
			CullingResults cullingResults,
			int renderingLayerMask,
			bool opaque,
			in CameraRendererTextures textures,
			in ShadowTextures shadowTextures)
		{
			ProfilingSampler sampler = opaque ? samplerOpaque : samplerTransparent;

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out GeometryPass pass, sampler);

			pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
				new RendererListDesc(shaderTagIDs, cullingResults, camera)
				{
					sortingCriteria = opaque ? SortingCriteria.CommonOpaque : SortingCriteria.CommonTransparent,
					rendererConfiguration =
						PerObjectData.ReflectionProbes |
						PerObjectData.Lightmaps |
						PerObjectData.ShadowMask |
						PerObjectData.LightProbe |
						PerObjectData.OcclusionProbe |
						PerObjectData.LightProbeProxyVolume |
						PerObjectData.OcclusionProbeProxyVolume,
					renderQueueRange = opaque ? RenderQueueRange.opaque : RenderQueueRange.transparent,
					renderingLayerMask = (uint) renderingLayerMask
				}));

			pass.colorAttachment = textures.colorAttachment;
			pass.depthAttachment = textures.depthAttachment;
			// Transparent pass: explicitly rebind color+depth to ensure depth from opaque/GBuffer is available
			// (DeferredLightingPass may have unbound depth, CopyAttachmentsPass may have changed RT state)
			pass.needRenderTargetSetup = !opaque;

			builder.ReadWriteTexture(textures.colorAttachment);
			builder.ReadWriteTexture(textures.depthAttachment);
			if (!opaque)
			{
				if (textures.colorCopy.IsValid())
				{
					builder.ReadTexture(textures.colorCopy);
				}

				if (textures.depthCopy.IsValid())
				{
					builder.ReadTexture(textures.depthCopy);
				}
			}

			builder.ReadTexture(shadowTextures.directionalAtlas);
			builder.ReadTexture(shadowTextures.otherAtlas);

			builder.SetRenderFunc<GeometryPass>(
				static (pass, context) => pass.Render(context));
		}
	}
}