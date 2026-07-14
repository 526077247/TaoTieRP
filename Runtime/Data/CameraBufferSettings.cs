using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public struct CameraBufferSettings
    {

        public bool allowHDR;

        public bool copyColor, copyColorReflection;

        [Tooltip("Copy opaque depth to _CameraDepthTexture before transparent pass. " +
                 "No MSAA: CopyTexture. MSAA: depth pre-pass (DepthOnly shader pass).")]
        public bool copyDepth, copyDepthReflection;

        public enum DepthPrimingMode
        {
            Auto,
            Forced
        }

        [Tooltip("Controls depth pre-pass (depth priming) for generating _CameraDepthTexture.\n" +
                 "Disabled: Never use a depth pre-pass; depth is copied from the camera depth attachment.\n" +
                 "Auto: Use a depth pre-pass only when the pipeline already needs one (e.g., MSAA with depth texture).\n" +
                 "Forced: Always use a depth pre-pass when a depth texture is needed, regardless of MSAA.")]
        public DepthPrimingMode depthPrimingMode;

        [Range(0.1f, 2f)] public float renderScale;

        public enum BicubicRescalingMode
        {
            Off,
            UpOnly,
            UpAndDown
        }

        public BicubicRescalingMode bicubicRescaling;

        public enum HighQualityAAMode
        {
            Off,
            TAA = 1,
            MSAA2x = 2,
            MSAA4x = 4,
            MSAA8x = 8,
        }

        [MSAAField]
        [Tooltip("High-quality anti-aliasing. MSAA not available in deferred mode.")]
        public HighQualityAAMode highQualityAA;

        [Serializable]
        public class TAASettings
        {
            [Tooltip("Jitter Scale controls the amplitude of the jitter offset. " +
                     "Lower values reduce visible jitter/flicker but weaken anti-aliasing (sharper edges).")]
            [Range(0.1f, 1f)] public float jitterScale = 1f;

            [Tooltip("Base Blend Factor controls how much the history frame contributes. " +
                     "Higher values make the image more stable but may cause ghosting during motion.")]
            [Range(0f, 0.99f)] public float baseBlendFactor = 0.9f;

            [Tooltip("Variance Clamp Scale controls the tightness of the neighborhood clamp. " +
                     "Lower values reduce ghosting more aggressively but may cause flickering. " +
                     "Higher values preserve more detail but allow more ghosting.")]
            [Range(0.001f, 10f)] public float varianceClampScale = 0.9f;
        }

        [VisibleIf(nameof(highQualityAA), ShowIfOperator.Equal, (int)HighQualityAAMode.TAA)]
        public TAASettings taaSettings;

        public enum PostProcessAA
        {
            Off,
            FXAA,
            SMAA
        }
        [Tooltip("Post-process anti-aliasing. SMAA is not supported on WebGL1/GLES2.")]
        public PostProcessAA postProcessAA;
        
        [VisibleIf(nameof(postProcessAA), ShowIfOperator.NotEqual, (int)PostProcessAA.SMAA)]
        [Tooltip("Strip SMAA shader passes and lookup textures from builds when Post-Process AA is not set to SMAA. " +
                 "Reduces build size by ~180KB. Disable if you plan to switch to SMAA at runtime.")]
        public bool stripSMAAWhenUnused;
    }
}
