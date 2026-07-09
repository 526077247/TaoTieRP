using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
	public class PostFXPass
	{
		static readonly ProfilingSampler
			groupSampler = new("Post FX"),
			finalSampler = new("Final Post FX");

		/// <summary>Zero-GC comparison: reads the raw m_Value from TextureHandle's internal ResourceHandle.</summary>
		static unsafe int GetHandleIndex(ref TextureHandle handle)
		{
			// TextureHandle contains ResourceHandle which starts with uint m_Value
			fixed (TextureHandle* p = &handle)
				return *(int*)p;
		}


		static readonly GraphicsFormat colorFormat =
			SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
				? GraphicsFormat.R16G16B16A16_SFloat
				: GraphicsFormat.R8G8B8A8_UNorm;

		static readonly GraphicsFormat smaaEdgeFormat = GraphicsFormat.R8G8_UNorm;

		private static readonly int
			_SMAAAreaTexId = Shader.PropertyToID("_SMAAAreaTex"),
			_SMAASearchTexId = Shader.PropertyToID("_SMAASearchTex");
		GraphicsFormat format;

		PostFXStack stack;

		int colorLUTResolution;

		enum ScaleMode
		{
			None,
			Linear,
			Bicubic
		}

		ScaleMode scaleMode;

		TextureHandle colorSource, colorGradingResult, scaledResult;
		TextureHandle smaaEdges, smaaWeights, smaaResult;

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;
			buffer.SetGlobalFloat(PostFXStack.finalSrcBlendId, 1f);
			buffer.SetGlobalFloat(PostFXStack.finalDstBlendId, 0f);
			stack.SourceSize = stack.BufferSize;

			var postProcessAA = stack.BufferSettings.postProcessAA;

			RenderTargetIdentifier finalSource;
			int finalPass;

			if (postProcessAA == CameraBufferSettings.PostProcessAA.FXAA)
			{
				finalSource = colorGradingResult;
				finalPass = stack.GetPassIndex(PostFXPassNames.FXAA);
				if (colorLUTResolution > 0)
					stack.Draw(buffer, colorSource, finalSource, stack.GetPassIndex(PostFXPassNames.ApplyColorGrading));
				else
					stack.Draw(buffer, colorSource, finalSource, stack.GetPassIndex(PostFXPassNames.Copy));
			}
			else if (postProcessAA == CameraBufferSettings.PostProcessAA.SMAA)
			{
				// Ensure lookup textures are created
				SMAATextures.EnsureCreated();
				buffer.SetGlobalTexture(_SMAAAreaTexId, SMAATextures.AreaTex);
				buffer.SetGlobalTexture(_SMAASearchTexId, SMAATextures.SearchTex);

				// Apply color grading first into colorGradingResult
				if (colorLUTResolution > 0)
					stack.Draw(buffer, colorSource, colorGradingResult, stack.GetPassIndex(PostFXPassNames.ApplyColorGrading));
				else
					stack.Draw(buffer, colorSource, colorGradingResult, stack.GetPassIndex(PostFXPassNames.Copy));

				// SMAA Pass 1: Edge Detection (color -> edges)
				stack.Draw(buffer, colorGradingResult, smaaEdges, stack.GetPassIndex(PostFXPassNames.SMAAEdgeDetection));

				// SMAA Pass 2: Blend Weight Calculation (edges -> weights)
				stack.Draw(buffer, smaaEdges, smaaWeights, stack.GetPassIndex(PostFXPassNames.SMAABlendWeightCalculation));

				// SMAA Pass 3: Neighborhood Blending (color + weights -> smaaResult)
				buffer.SetGlobalTexture(PostFXStack.fxSource2Id, smaaWeights);
				stack.Draw(buffer, colorGradingResult, smaaResult, stack.GetPassIndex(PostFXPassNames.SMAANeighborhoodBlending));

				finalSource = smaaResult;
				finalPass = stack.GetPassIndex(PostFXPassNames.Copy);
			}
			else
			{
				finalSource = colorSource;
				finalPass = colorLUTResolution > 0
					? stack.GetPassIndex(PostFXPassNames.ApplyColorGrading)
					: stack.GetPassIndex(PostFXPassNames.Copy);
			}

			if (scaleMode == ScaleMode.None)
			{
				stack.DrawFinal(buffer, finalSource, finalPass);
			}
			else
			{
				stack.Draw(buffer, finalSource, scaledResult, finalPass);
				stack.DrawFinal(buffer, scaledResult,
					scaleMode == ScaleMode.Bicubic
						? stack.GetPassIndex(PostFXPassNames.FinalRescale)
						: stack.GetPassIndex(PostFXPassNames.Copy));
			}

			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}

		public static bool Record(
			RenderGraph renderGraph,
			PostFXStack stack,
			int colorLUTResolution,
			in CameraRendererTextures textures)
		{
			using var _ = new RenderGraphProfilingScope(renderGraph, groupSampler);

			stack.ColorLUTResolution = colorLUTResolution;

			TextureHandle source = textures.resolvedColorAttachment;
			TextureHandle colorLUT = default;
			bool hasColorGrading = false;
			bool anyEffectRan = false;

			// Iterate over PostFXSettings effects list (in serialized order)
			var effects = stack.Settings.Effects;
			int effectCount = effects.Count;
			for (int i = 0; i < effectCount; i++)
			{
				var effect = effects[i];
				if (effect == null) continue;
				// Track whether this effect changed source (zero-GC: compare raw struct bytes)
				int prevIdx = GetHandleIndex(ref source);
				source = effect.Execute(renderGraph, stack, source, textures);
				int curIdx = GetHandleIndex(ref source);
				if (curIdx != prevIdx)
					anyEffectRan = true;

				if (effect is ColorGradingEffect cgEffect)
				{
					hasColorGrading = cgEffect.IsEnabled;
					colorLUT = cgEffect.ColorGradingLUT;
				}
			}

			if (!anyEffectRan)
				return false;

			int effectiveLUTResolution = (hasColorGrading && colorLUT.IsValid() && colorLUTResolution > 0)
				? colorLUTResolution : 0;

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				finalSampler.name, out PostFXPass pass, finalSampler);
			pass.stack = stack;
			pass.colorSource = builder.ReadTexture(source);
			pass.colorLUTResolution = effectiveLUTResolution;
			if (effectiveLUTResolution > 0)
			{
				builder.ReadTexture(colorLUT);
			}

			if (stack.BufferSize.x == stack.Camera.pixelWidth)
			{
				pass.scaleMode = ScaleMode.None;
			}
			else
			{
				pass.scaleMode =
					stack.BufferSettings.bicubicRescaling ==
					CameraBufferSettings.BicubicRescalingMode.UpAndDown ||
					stack.BufferSettings.bicubicRescaling ==
					CameraBufferSettings.BicubicRescalingMode.UpOnly &&
					stack.BufferSize.x < stack.Camera.pixelWidth
						? ScaleMode.Bicubic
						: ScaleMode.Linear;
			}

			bool applyFXAA = stack.BufferSettings.postProcessAA == CameraBufferSettings.PostProcessAA.FXAA;
			bool applySMAA = stack.BufferSettings.postProcessAA == CameraBufferSettings.PostProcessAA.SMAA;
			bool needsColorGradingResult = applyFXAA || applySMAA;
			if (needsColorGradingResult || pass.scaleMode != ScaleMode.None)
			{
				pass.format = colorFormat;
				var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = pass.format
				};
				if (needsColorGradingResult)
				{
					desc.name = "Color Grading Result";
					pass.colorGradingResult = builder.CreateTransientTexture(desc);
				}

				if (pass.scaleMode != ScaleMode.None)
				{
					desc.name = "Scaled Result";
					pass.scaledResult = builder.CreateTransientTexture(desc);
				}
			}

			if (applySMAA)
			{
				var edgeDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = smaaEdgeFormat,
					name = "SMAA Edges"
				};
				pass.smaaEdges = builder.CreateTransientTexture(edgeDesc);

				var weightDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = pass.format,
					name = "SMAA Weights"
				};
				pass.smaaWeights = builder.CreateTransientTexture(weightDesc);

				var resultDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = pass.format,
					name = "SMAA Result"
				};
				pass.smaaResult = builder.CreateTransientTexture(resultDesc);
			}

			builder.SetRenderFunc<PostFXPass>(
				static (pass, context) => pass.Render(context));

			return true;
		}
	}
}
