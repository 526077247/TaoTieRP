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
            FXAA,
            ApplyColorGradingWithLuma,
            FXAAWithLuma
        }

        public static readonly int
            fxSourceId = Shader.PropertyToID("_PostFXSource"),
            fxSource2Id = Shader.PropertyToID("_PostFXSource2"),
            finalSrcBlendId = Shader.PropertyToID("_FinalSrcBlend"),
            finalDstBlendId = Shader.PropertyToID("_FinalDstBlend");

        static readonly Rect fullViewRect = new(0f, 0f, 1f, 1f);

        public CameraBufferSettings BufferSettings { get; set; }

        public Vector2Int BufferSize { get; set; }

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
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                FinalBlendMode.destination == BlendMode.Zero &&
                Camera.rect == fullViewRect
                    ? RenderBufferLoadAction.DontCare
                    : RenderBufferLoadAction.Load,
                RenderBufferStoreAction.Store);
            buffer.SetViewport(Camera.pixelRect);
            
            buffer.Blit(from, BuiltinRenderTextureType.CameraTarget, Settings.Material, (int) pass);
        }
    }
}