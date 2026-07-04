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
        
        public CameraBufferSettings cameraBuffer = new()
        {
            allowHDR = true,
            msaa = MSAASamples.None,
            renderScale = 1f,
        };

        public bool useSRPBatcher = true;

        public ShadowSettings shadows;

        public PostFXSettings postFXSettings;

        public enum ColorLUTResolution
        {
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("Off")]
#endif
            Off = 0,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("16")]
#endif
            _16 = 16,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("32")]
#endif
            _32 = 32,
#if ODIN_INSPECTOR
            [Sirenix.OdinInspector.LabelText("64")]
#endif
            _64 = 64
        }

        public ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;
        [HideInInspector]
        public Material defaultMaterial;

        [HideInInspector]
        public Shader cameraRendererShader, cameraDebuggerShader;
    }
}