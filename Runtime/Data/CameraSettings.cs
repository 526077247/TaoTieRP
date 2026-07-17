using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public class CameraSettings
    {
        public enum RenderingMode
        {
            Base,
            [Tooltip("Render Direct To Screen: renders directly to CameraTarget without temp RT or FinalPass blit. Skips PostFX/AA/SSAO/TAA/Skybox/DepthPre/Resolve/Copy.")]
            RenderDirectToScreen
        }

        public RenderingMode renderingMode = RenderingMode.Base;
        
        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        public bool copyColor = true, copyDepth = true;

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        public bool maskLights = false;
        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        [RenderingLayerMaskField]
        public int renderingLayerMask = -1;
        
        public enum RenderScaleMode
        {
            Inherit,
            Multiply,
            Override
        }

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;
        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        [ShowIf(nameof(renderScaleMode), ShowIfOperator.NotEqual, (int)RenderScaleMode.Inherit)]
        [Range(0.1f, 2f)] public float renderScale = 1f;
        
        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        [Tooltip("Which Volume layers affect this camera. Everything = all layers.")]
        public LayerMask volumeLayerMask = ~0;

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        public bool overridePostFX = false;

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        [ShowIf(nameof(overridePostFX))]
        public PostFXSettings postFXSettings = default;

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        [MSAAField]
        [Tooltip("Allow high-quality anti-aliasing for this camera.")]
        public bool allowHighQualityAA = true;

        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
        public bool allowPostProcessAA = true;

        [Serializable]
        public struct FinalBlendMode
        {
            public BlendMode source, destination;
        }
        
        [ShowIf(nameof(renderingMode), ShowIfOperator.Equal, (int)RenderingMode.Base)]
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
