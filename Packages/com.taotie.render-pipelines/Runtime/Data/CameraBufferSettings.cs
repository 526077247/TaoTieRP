using UnityEngine;
using System;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public struct CameraBufferSettings
    {

        public bool allowHDR;

        public bool copyColor, copyColorReflection, copyDepth, copyDepthReflection;

        [Range(0.1f, 2f)] public float renderScale;

        public enum BicubicRescalingMode
        {
            Off,
            UpOnly,
            UpAndDown
        }

        public BicubicRescalingMode bicubicRescaling;

        [Serializable]
        public struct FXAA
        {
            public bool enabled;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf(nameof(enabled))]
#endif
            [Range(0.0312f, 0.0833f)] public float fixedThreshold;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf(nameof(enabled))]
#endif
            [Range(0.063f, 0.333f)] public float relativeThreshold;
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf(nameof(enabled))]
#endif
            [Range(0f, 1f)] public float subpixelBlending;

            public enum Quality
            {
                Low,
                Medium,
                High
            }
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.ShowIf(nameof(enabled))]
#endif
            public Quality quality;
        }

        public FXAA fxaa;

        public bool outLine;
    }
}