using UnityEngine;
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

		static Mesh fullscreenMesh;

		public static Mesh FullscreenMesh
		{
			get
			{
				if (fullscreenMesh == null)
				{
					fullscreenMesh = new Mesh { name = "Fullscreen Quad" };
					fullscreenMesh.vertices = new Vector3[] {
						new(-1, -1, 0), new(1, -1, 0), new(-1, 1, 0), new(1, 1, 0)
					};
					fullscreenMesh.uv = new Vector2[] {
						new(0, 0), new(1, 0), new(0, 1), new(1, 1)
					};
					fullscreenMesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
				}
				return fullscreenMesh;
			}
		}

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
				CopyByDrawing(buffer, from, to, isDepth,
					new Rect(0, 0, camera.pixelWidth, camera.pixelHeight));
			}
		}

		public readonly void CopyByDrawing(
			CommandBuffer buffer,
			RenderTargetIdentifier from,
			RenderTargetIdentifier to,
			bool isDepth,
			Rect viewport)
		{
			buffer.SetGlobalTexture(sourceTextureID, from);
			buffer.SetRenderTarget(
				to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
			buffer.SetViewport(viewport);
			buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
			buffer.DrawMesh(FullscreenMesh, Matrix4x4.identity, material, 0, isDepth ? 1 : 0);
			buffer.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
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
			buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
			buffer.DrawMesh(FullscreenMesh, Matrix4x4.identity, material, 0, 0);
			buffer.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);
			buffer.SetGlobalFloat(srcBlendID, 1f);
			buffer.SetGlobalFloat(dstBlendID, 0f);
		}
	}
}