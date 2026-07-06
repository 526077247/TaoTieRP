using UnityEngine;
using UnityEngine.Rendering;
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

        [MSAAField]
        [Tooltip("Multi-sample anti-aliasing. Not available in deferred rendering mode.")]
        public MSAASamples msaa;

        public bool fxaa;

        public bool outLine;
    }
}