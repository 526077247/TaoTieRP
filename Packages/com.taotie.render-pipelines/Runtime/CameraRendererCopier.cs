﻿using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
	public readonly struct CameraRendererCopier
	{
		static readonly int
			sourceTextureID = Shader.PropertyToID("_SourceTexture"),
			srcBlendID = Shader.PropertyToID("_CameraSrcBlend"),
			dstBlendID = Shader.PropertyToID("_CameraDstBlend");

		static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);

		static readonly bool copyTextureSupported =
			SystemInfo.copyTextureSupport > CopyTextureSupport.None;

		public static bool RequiresRenderTargetResetAfterCopy => !copyTextureSupported;

		public Camera Camera => camera;

		readonly Material material;

		readonly Camera camera;

		readonly CameraSettings.FinalBlendMode finalBlendMode;

		public CameraRendererCopier(
			Material material, Camera camera, CameraSettings.FinalBlendMode finalBlendMode)
		{
			this.material = material;
			this.camera = camera;
			this.finalBlendMode = finalBlendMode;
		}

		public readonly void Copy(
			CommandBuffer buffer,
			RenderTargetIdentifier from,
			RenderTargetIdentifier to,
			bool isDepth)
		{
			if (copyTextureSupported)
			{
				buffer.CopyTexture(from, to);
			}
			else
			{
				CopyByDrawing(buffer, from, to, isDepth);
			}
		}

		public readonly void CopyByDrawing(
			CommandBuffer buffer,
			RenderTargetIdentifier from,
			RenderTargetIdentifier to,
			bool isDepth)
		{
			buffer.SetGlobalTexture(sourceTextureID, from);
			buffer.SetRenderTarget(
				to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
			buffer.SetViewport(camera.pixelRect);
			buffer.Blit(from, to, material, isDepth ? 1 : 0);
		}

		public readonly void CopyToCameraTarget(
			CommandBuffer buffer,
			RenderTargetIdentifier from)
		{
			buffer.SetGlobalFloat(srcBlendID, (float) finalBlendMode.source);
			buffer.SetGlobalFloat(dstBlendID, (float) finalBlendMode.destination);
			buffer.SetGlobalTexture(sourceTextureID, from);
			buffer.SetRenderTarget(
				BuiltinRenderTextureType.CameraTarget,
				finalBlendMode.destination == BlendMode.Zero && camera.rect == fullViewRect
					? RenderBufferLoadAction.DontCare
					: RenderBufferLoadAction.Load,
				RenderBufferStoreAction.Store);
			buffer.SetViewport(camera.pixelRect);
			buffer.Blit(from, BuiltinRenderTextureType.CameraTarget, material, 0);
			buffer.SetGlobalFloat(srcBlendID, 1f);
			buffer.SetGlobalFloat(dstBlendID, 0f);
		}
	}
}