﻿using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public class CameraSettings
    {

        public bool copyColor = true, copyDepth = true;

        [RenderingLayerMaskField] public int renderingLayerMask = -1;

        public bool maskLights = false;

        public enum RenderScaleMode
        {
            Inherit,
            Multiply,
            Override
        }

        public RenderScaleMode renderScaleMode = RenderScaleMode.Inherit;
#if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowIf("@"+nameof(renderScaleMode)+"!=RenderScaleMode.Inherit")]
#endif
        [Range(0.1f, 2f)] public float renderScale = 1f;

        public bool overridePostFX = false;

        #if ODIN_INSPECTOR
        [Sirenix.OdinInspector.ShowIf(nameof(overridePostFX))]
        #endif
        public PostFXSettings postFXSettings = default;

        public bool allowFXAA = true;

        public bool keepAlpha = true;

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