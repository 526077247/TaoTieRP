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
            MSAA,
            TAA
        }

        [MSAAField]
        [Tooltip("High-quality anti-aliasing. MSAA not available in deferred mode.")]
        public HighQualityAAMode highQualityAA;

        [Tooltip("MSAA sample count when High-Quality AA is set to MSAA.")]
        [ShowIf(nameof(highQualityAA), ShowIfOperator.Equal, (int)HighQualityAAMode.MSAA)]
        [MSAAField]
        public MSAASamples msaaSamples;

        [Serializable]
        public class TAASettings
        {
            [Tooltip("Jitter Scale controls the amplitude of the jitter offset. " +
                     "Lower values reduce visible jitter/flicker but weaken anti-aliasing (sharper edges).")]
            [Range(0.1f, 1f)] public float jitterScale = 0.5f;

            [Tooltip("Anti-Flicker expands the neighborhood clamp bounds to reduce flickering. " +
                     "Higher values suppress flicker but may introduce ghosting.")]
            [Range(0f, 1f)] public float antiFlicker = 0.125f;

            [Tooltip("Base Blend Factor controls how much the history frame contributes. " +
                     "Higher values make the image more stable but may cause ghosting during motion.")]
            [Range(0f, 0.99f)] public float baseBlendFactor = 0.9f;

            [Tooltip("Jitter Spread controls the diameter of the jitter sample spread. " +
                     "Smaller values produce sharper but more aliased images; " +
                     "larger values produce smoother but blurrier images.")]
            [Range(0.1f, 2f)] public float jitterSpread = 1f;
        }

        [ShowIf(nameof(highQualityAA), ShowIfOperator.Equal, (int)HighQualityAAMode.TAA)]
        public TAASettings taaSettings;

        public enum PostProcessAA
        {
            Off,
            FXAA,
            SMAA
        }
        public PostProcessAA postProcessAA;
        
        public bool outLine;
    }
}
