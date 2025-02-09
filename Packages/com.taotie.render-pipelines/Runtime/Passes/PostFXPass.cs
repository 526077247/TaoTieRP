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

		static readonly int
			copyBicubicId = Shader.PropertyToID("_CopyBicubic"),
			fxaaConfigId = Shader.PropertyToID("_FXAAConfig");

		static readonly GlobalKeyword
			fxaaLowKeyword = GlobalKeyword.Create("FXAA_QUALITY_LOW"),
			fxaaMediumKeyword = GlobalKeyword.Create("FXAA_QUALITY_MEDIUM");

		static readonly GraphicsFormat colorFormat =
			SystemInfo.GetGraphicsFormat(DefaultFormat.LDR);

		PostFXStack stack;

		bool keepAlpha;

		enum ScaleMode
		{
			None,
			Linear,
			Bicubic
		}

		ScaleMode scaleMode;

		TextureHandle colorSource, colorGradingResult, scaledResult;

		void ConfigureFXAA(CommandBuffer buffer)
		{
			CameraBufferSettings.FXAA fxaa = stack.BufferSettings.fxaa;
			buffer.SetKeyword(fxaaLowKeyword, fxaa.quality ==
			                                  CameraBufferSettings.FXAA.Quality.Low);
			buffer.SetKeyword(fxaaMediumKeyword, fxaa.quality ==
			                                     CameraBufferSettings.FXAA.Quality.Medium);
			buffer.SetGlobalVector(fxaaConfigId, new Vector4(
				fxaa.fixedThreshold,
				fxaa.relativeThreshold,
				fxaa.subpixelBlending));
		}

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;
			buffer.SetGlobalFloat(PostFXStack.finalSrcBlendId, 1f);
			buffer.SetGlobalFloat(PostFXStack.finalDstBlendId, 0f);

			RenderTargetIdentifier finalSource;
			PostFXStack.Pass finalPass;
			if (stack.BufferSettings.fxaa.enabled)
			{
				finalSource = colorGradingResult;
				finalPass = keepAlpha ? PostFXStack.Pass.FXAA : PostFXStack.Pass.FXAAWithLuma;
				ConfigureFXAA(buffer);
				stack.Draw(buffer, colorSource, finalSource,
					keepAlpha ? PostFXStack.Pass.ApplyColorGrading : PostFXStack.Pass.ApplyColorGradingWithLuma);
			}
			else
			{
				finalSource = colorSource;
				finalPass = PostFXStack.Pass.ApplyColorGrading;
			}

			if (scaleMode == ScaleMode.None)
			{
				stack.DrawFinal(buffer, finalSource, finalPass);
			}
			else
			{
				stack.Draw(buffer, finalSource, scaledResult, finalPass);
				buffer.SetGlobalFloat(copyBicubicId,
					scaleMode == ScaleMode.Bicubic ? 1f : 0f);
				stack.DrawFinal(buffer, scaledResult, PostFXStack.Pass.FinalRescale);
			}

			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}

		public static void Record(
			RenderGraph renderGraph,
			PostFXStack stack,
			int colorLUTResolution,
			bool keepAlpha,
			in CameraRendererTextures textures)
		{
			using var _ = new RenderGraphProfilingScope(renderGraph, groupSampler);

			TextureHandle colorSource = BloomPass.Record(
				renderGraph, stack, textures);

			TextureHandle colorLUT = ColorLUTPass.Record(
				renderGraph, stack, colorLUTResolution);

			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				finalSampler.name, out PostFXPass pass, finalSampler);
			pass.keepAlpha = keepAlpha;
			pass.stack = stack;
			pass.colorSource = builder.ReadTexture(colorSource);
			builder.ReadTexture(colorLUT);

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

			bool applyFXAA = stack.BufferSettings.fxaa.enabled;
			if (applyFXAA || pass.scaleMode != ScaleMode.None)
			{
				var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
				{
					colorFormat = colorFormat
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