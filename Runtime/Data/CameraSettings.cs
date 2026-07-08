using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public class CameraSettings
    {

        public bool copyColor = true, copyDepth = true;

        public bool useRenderingLayerMask = false;
        [ShowIf(nameof(useRenderingLayerMask))]
        [RenderingLayerMaskField]
        public int renderingLayerMask = -1;

        public enum RenderScaleMode
        {
            Inherit,
            Multiply,
            Override
        }

        public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;
        [ShowIf(nameof(renderScaleMode), ShowIfOperator.NotEqual, (int)RenderScaleMode.Inherit)]
        [Range(0.1f, 2f)] public float renderScale = 1f;

        public bool overridePostFX = false;

        [ShowIf(nameof(overridePostFX))]
        public PostFXSettings postFXSettings = default;

        public bool allowPostProcessAA = true;

        [MSAAField]
        [Tooltip("Allow high-quality anti-aliasing for this camera.")]
        public bool allowHighQualityAA = true;

        [Serializable]
        public struct FinalBlendMode
        {
            public BlendMode source, destination;
        }

        public FinalBlendMode finalBlendMode = new FinalBlendMode
        {
            source = BlendMode.One,
            destination = BlendMode.Zero
        };

        public float GetRenderScale(float scale)
        {
            return
                renderScaleMode == RenderScaleMode.Inherit ? scale :
                renderScaleMode == RenderScaleMode.Override ? renderScale :
                scale * renderScale;
        }
    }
}
