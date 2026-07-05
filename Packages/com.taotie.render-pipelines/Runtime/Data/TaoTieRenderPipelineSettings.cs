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
            msaa = MSAASamples.None,
            renderScale = 1f,
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

        [HideInInspector]
        public Shader cameraRendererShader;
    }
}
