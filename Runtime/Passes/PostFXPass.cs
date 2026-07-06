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
			PostFXStack.Pass finalPass;

			if (postProcessAA == CameraBufferSettings.PostProcessAA.FXAA)
			{
				finalSource = colorGradingResult;
				finalPass = PostFXStack.Pass.FXAA;
				if (colorLUTResolution > 0)
					stack.Draw(buffer, colorSource, finalSource, PostFXStack.Pass.ApplyColorGrading);
				else
					stack.Draw(buffer, colorSource, finalSource, PostFXStack.Pass.Copy);
			}
			else if (postProcessAA == CameraBufferSettings.PostProcessAA.SMAA)
			{
				// Ensure lookup textures are created
				SMAATextures.EnsureCreated();
				buffer.SetGlobalTexture(_SMAAAreaTexId, SMAATextures.AreaTex);
				buffer.SetGlobalTexture(_SMAASearchTexId, SMAATextures.SearchTex);

				// Apply color grading first into colorGradingResult
				if (colorLUTResolution > 0)
					stack.Draw(buffer, colorSource, colorGradingResult, PostFXStack.Pass.ApplyColorGrading);
				else
					stack.Draw(buffer, colorSource, colorGradingResult, PostFXStack.Pass.Copy);

				// SMAA Pass 1: Edge Detection (color → edges)
				stack.Draw(buffer, colorGradingResult, smaaEdges, PostFXStack.Pass.SMAAEdgeDetection);

				// SMAA Pass 2: Blend Weight Calculation (edges → weights)
				stack.Draw(buffer, smaaEdges, smaaWeights, PostFXStack.Pass.SMAABlendWeightCalculation);

				// SMAA Pass 3: Neighborhood Blending (color + weights → smaaResult)
				buffer.SetGlobalTexture(PostFXStack.fxSource2Id, smaaWeights);
				stack.Draw(buffer, colorGradingResult, smaaResult, PostFXStack.Pass.SMAANeighborhoodBlending);

				finalSource = smaaResult;
				finalPass = PostFXStack.Pass.Copy;
			}
			else
			{
				finalSource = colorSource;
				finalPass = colorLUTResolution > 0 ? PostFXStack.Pass.ApplyColorGrading : PostFXStack.Pass.Copy;
			}

			if (scaleMode == ScaleMode.None)
			{
				stack.DrawFinal(buffer, finalSource, finalPass);
			}
			else
			{
				stack.Draw(buffer, finalSource, scaledResult, finalPass);
				stack.DrawFinal(buffer, scaledResult,
					scaleMode == ScaleMode.Bicubic ? PostFXStack.Pass.FinalRescale : PostFXStack.Pass.Copy);
			}

			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}

		public static void Record(
			RenderGraph renderGraph,
			PostFXStack stack,
			int colorLUTResolution,
			in CameraRendererTextures textures)
		{
			using var _ = new RenderGraphProfilingScope(renderGraph, groupSampler);

			TextureHandle colorSource = BloomPass.Record(renderGraph, stack, textures);

			TextureHandle colorLUT = default;
			if (colorLUTResolution > 0)
			{
				colorLUT = ColorLUTPass.Record(renderGraph, stack, colorLUTResolution);
			}

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				finalSampler.name, out PostFXPass pass, finalSampler);
			pass.stack = stack;
			pass.colorSource = builder.ReadTexture(colorSource);
			pass.colorLUTResolution = colorLUTResolution;
			if (colorLUTResolution > 0)
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
		}
	}
}
