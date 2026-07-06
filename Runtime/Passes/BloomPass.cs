using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
	public class BloomPass
	{
		const int maxBloomPyramidLevels = 16;

		static readonly int
			bicubicUpsamplingId =
				Shader.PropertyToID("_BloomBicubicUpsampling"),
			intensityId = Shader.PropertyToID("_BloomIntensity"),
			thresholdId = Shader.PropertyToID("_BloomThreshold");

		static readonly ProfilingSampler sampler = new("Bloom");

		static readonly TextureHandle[] pyramid =
			new TextureHandle[2 * maxBloomPyramidLevels + 1];

		static readonly Vector2Int[] pyramidSizes =
			new Vector2Int[2 * maxBloomPyramidLevels + 1];

		Vector2Int sourceSize;

		TextureHandle colorSource, bloomResult;

		PostFXStack stack;

		int stepCount;

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;
			PostFXSettings.BloomSettings bloom = stack.Settings.Bloom;

			Vector4 threshold;
			threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
			threshold.y = threshold.x * bloom.thresholdKnee;
			threshold.z = 2f * threshold.y;
			threshold.w = 0.25f / (threshold.y + 0.00001f);
			threshold.y -= threshold.x;
			buffer.SetGlobalVector(thresholdId, threshold);

			stack.SourceSize = sourceSize;
			stack.Draw(buffer, colorSource, pyramid[0],
				bloom.fadeFireflies ? PostFXStack.Pass.BloomPrefilterFireflies : PostFXStack.Pass.BloomPrefilter);

			int fromId = 0, toId = 2;
			int i;
			for (i = 0; i < stepCount; i++)
			{
				int midId = toId - 1;
				stack.SourceSize = pyramidSizes[fromId];
				stack.Draw(buffer, pyramid[fromId], pyramid[midId],
					PostFXStack.Pass.BloomHorizontal);
				stack.SourceSize = pyramidSizes[midId];
				stack.Draw(buffer, pyramid[midId], pyramid[toId],
					PostFXStack.Pass.BloomVertical);
				fromId = toId;
				toId += 2;
			}

			buffer.SetGlobalFloat(
				bicubicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f);

			PostFXStack.Pass combinePass, finalPass;
			float finalIntensity;
			if (bloom.mode == PostFXSettings.BloomSettings.Mode.Additive)
			{
				combinePass = finalPass = PostFXStack.Pass.BloomAdd;
				buffer.SetGlobalFloat(intensityId, 1f);
				finalIntensity = bloom.intensity;
			}
			else
			{
				combinePass = PostFXStack.Pass.BloomScatter;
				finalPass = PostFXStack.Pass.BloomScatterFinal;
				buffer.SetGlobalFloat(intensityId, bloom.scatter);
				finalIntensity = Mathf.Min(bloom.intensity, 1f);
			}

			if (i > 1)
			{
				toId -= 5;
				for (i -= 1; i > 0; i--)
				{
					buffer.SetGlobalTexture(PostFXStack.fxSource2Id, pyramid[toId + 1]);
					stack.SourceSize = pyramidSizes[fromId];
					stack.Draw(buffer, pyramid[fromId], pyramid[toId], combinePass);
					fromId = toId;
					toId -= 2;
				}
			}

			buffer.SetGlobalFloat(intensityId, finalIntensity);
			buffer.SetGlobalTexture(PostFXStack.fxSource2Id, colorSource);
			stack.SourceSize = pyramidSizes[fromId];
			stack.Draw(buffer, pyramid[fromId], bloomResult, finalPass);
		}

		public static TextureHandle Record(
			RenderGraph renderGraph,
			PostFXStack stack,
			in CameraRendererTextures textures)
		{
			PostFXSettings.BloomSettings bloom = stack.Settings.Bloom;
			if (bloom.maxIterations == 0 ||
			    bloom.intensity <= 0f)
			{
				return textures.resolvedColorAttachment;
			}

			Vector2Int sourceSize = bloom.ignoreRenderScale
				? new Vector2Int(stack.Camera.pixelWidth, stack.Camera.pixelHeight)
				: stack.BufferSize;
			Vector2Int size = sourceSize / 2;

			if (size.y < bloom.downscaleLimit * 2 ||
			    size.x < bloom.downscaleLimit * 2)
			{
				return textures.resolvedColorAttachment;
			}

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out BloomPass pass, sampler);
			pass.stack = stack;
			pass.colorSource = builder.ReadTexture(textures.resolvedColorAttachment);
			pass.sourceSize = sourceSize;

			var desc = new TextureDesc(size.x, size.y)
			{
				colorFormat = SystemInfo.GetGraphicsFormat(
					stack.BufferSettings.allowHDR ? DefaultFormat.HDR : DefaultFormat.LDR),
				name = "Bloom Prefilter"
			};
			pyramid[0] = builder.CreateTransientTexture(desc);
			pyramidSizes[0] = size;
			size /= 2;

			int pyramidIndex = 1;
			int i;
			for (i = 0; i < bloom.maxIterations; i++, pyramidIndex += 2)
			{
				if (size.y < bloom.downscaleLimit || size.x < bloom.downscaleLimit)
				{
					break;
				}

				desc.width = size.x;
				desc.height = size.y;
				desc.name = "Bloom Pyramid H";
				pyramid[pyramidIndex] = builder.CreateTransientTexture(desc);
				pyramidSizes[pyramidIndex] = size;
				desc.name = "Bloom Pyramid V";
				pyramid[pyramidIndex + 1] = builder.CreateTransientTexture(desc);
				pyramidSizes[pyramidIndex + 1] = size;
				size /= 2;
			}

			pass.stepCount = i;

			desc.width = stack.BufferSize.x;
			desc.height = stack.BufferSize.y;
			desc.name = "Bloom Result";
			pass.bloomResult = builder.WriteTexture(
				renderGraph.CreateTexture(desc));
			builder.SetRenderFunc<BloomPass>(
				static (pass, context) => pass.Render(context));
			return pass.bloomResult;
		}
	}
}