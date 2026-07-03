using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class PostFXStack
    {
        public enum Pass
        {
            Copy,
            BloomHorizontal,
            BloomVertical,
            BloomAdd,
            BloomPrefilter,
            BloomPrefilterFireflies,
            BloomScatter,
            BloomScatterFinal,
            ColorGradingNone,
            ColorGradingACES,
            ColorGradingNeutral,
            ColorGradingReinhard,
            ApplyColorGrading,
            FinalRescale,
            FXAA
        }

        public static readonly int
            fxSourceId = Shader.PropertyToID("_PostFXSource"),
            fxSource2Id = Shader.PropertyToID("_PostFXSource2"),
            fxSourceTexelSizeId = Shader.PropertyToID("_PostFXSource_TexelSize"),
            finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
            finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

        static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);

        public CameraBufferSettings BufferSettings { get; set; }

        public Vector2Int BufferSize { get; set; }

        public Vector2Int SourceSize { get; set; }

        public Camera Camera { get; set; }

        public CameraSettings.FinalBlendMode FinalBlendMode { get; set; }

        public PostFXSettings Settings { get; set; }

        public void Draw(CommandBuffer buffer, RenderTargetIdentifier to, Pass pass)
        {
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            buffer.Blit(null, to, Settings.Material, (int) pass);
        }

        public void Draw(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            RenderTargetIdentifier to,
            Pass pass)
        {
            buffer.SetGlobalTexture(fxSourceId, from);
            buffer.SetGlobalVector(fxSourceTexelSizeId, new Vector4(
                1f / SourceSize.x, 1f / SourceSize.y, SourceSize.x, SourceSize.y));
            buffer.SetRenderTarget(
                to, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            
            buffer.Blit(from, to, Settings.Material, (int) pass);
        }

        public void DrawFinal(
            CommandBuffer buffer,
            RenderTargetIdentifier from,
            Pass pass)
        {
            buffer.SetGlobalFloat(finalSrcBlendId, (float) FinalBlendMode.source);
            buffer.SetGlobalFloat(
                finalDstBlendId, (float) FinalBlendMode.destination);
            buffer.SetGlobalTexture(fxSourceId, from);
            buffer.SetGlobalVector(fxSourceTexelSizeId, new Vector4(
                1f / SourceSize.x, 1f / SourceSize.y, SourceSize.x, SourceSize.y));
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                FinalBlendMode.destination == BlendMode.Zero &&
                Camera.rect == fullViewRect
                    ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store);
            buffer.SetViewport(Camera.pixelRect);
            buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            buffer.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, Settings.Material, 0, (int) pass);
            buffer.SetViewProjectionMatrices(Camera.worldToCameraMatrix, Camera.projectionMatrix);
        }
    }
}