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

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;
			buffer.SetGlobalFloat(PostFXStack.finalSrcBlendId, 1f);
			buffer.SetGlobalFloat(PostFXStack.finalDstBlendId, 0f);
			stack.SourceSize = stack.BufferSize;

			RenderTargetIdentifier finalSource;
			PostFXStack.Pass finalPass;
			if (stack.BufferSettings.fxaa)
			{
				finalSource = colorGradingResult;
				finalPass = PostFXStack.Pass.FXAA;
				if (colorLUTResolution > 0)
				{
					stack.Draw(buffer, colorSource, finalSource,
						PostFXStack.Pass.ApplyColorGrading);
				}
				else
				{
					stack.Draw(buffer, colorSource, finalSource, PostFXStack.Pass.Copy);
				}
			}
			else
			{
				finalSource = colorSource;
				finalPass = colorLUTResolution > 0?PostFXStack.Pass.ApplyColorGrading: PostFXStack.Pass.Copy;
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

			bool applyFXAA = stack.BufferSettings.fxaa;
			if (applyFXAA || pass.scaleMode != ScaleMode.None)
			{
				pass.format = colorFormat;
				var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = pass.format
				};
				if (applyFXAA)
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

			builder.SetRenderFunc<PostFXPass>(
				static (pass, context) => pass.Render(context));
		}
	}
}