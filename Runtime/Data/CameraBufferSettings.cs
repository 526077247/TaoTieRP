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

        [MSAAField]
        [Tooltip("Multi-sample anti-aliasing. Not available in deferred rendering mode or WebGL1/GLES2.")]
        public MSAASamples msaa;

        public bool fxaa;

        public bool outLine;
    }
}