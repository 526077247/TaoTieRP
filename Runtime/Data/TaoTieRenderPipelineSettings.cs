using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public class TaoTieRenderPipelineSettings
    {
        public enum RenderingMode
        {
            Forward,
            Deferred
        }

        [RenderingModeField]
        public RenderingMode renderingMode = RenderingMode.Forward;
        
        public bool useSRPBatcher = true;
        
        public CameraBufferSettings cameraBuffer = new()
        {
            allowHDR = true,
            copyColor = true,
            copyDepth = true,
            depthPrimingMode = CameraBufferSettings.DepthPrimingMode.Auto,
            renderScale = 1f,
            bicubicRescaling = CameraBufferSettings.BicubicRescalingMode.UpOnly,
            highQualityAA = CameraBufferSettings.HighQualityAAMode.Off,
            taaSettings = new CameraBufferSettings.TAASettings(),
        };

        public ShadowSettings shadows;

        public PostFXSettings postFXSettings;

        public enum ColorLUTResolution
        {
            [LabelText("Off")] Off = 0,
            [LabelText("16")] _16 = 16,
            [LabelText("32")] _32 = 32,
            [LabelText("64")] _64 = 64
        }

        [EnumLabel]
        public ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;

        [HideInInspector]
        public Material defaultMaterial;

        [HideInInspector] public Shader cameraRendererShader;
        [HideInInspector] public Shader deferredLightingShader;
        [HideInInspector] public Shader taaShader;
        [HideInInspector] public Shader forwardPlusDebuggerShader;
        [HideInInspector] public Shader depthDebuggerShader;
        [HideInInspector] public Shader overdrawResolveShader;

#if !UNITY_WEBGL || UNITY_EDITOR
        [HideInInspector] public ComputeShader forwardPlusCullCompute;
#endif
    }
}
