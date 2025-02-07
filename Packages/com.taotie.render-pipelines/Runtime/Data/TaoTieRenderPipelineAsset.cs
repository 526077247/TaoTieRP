using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie
{
    [CreateAssetMenu(menuName = "Rendering/TaoTie Pipeline")]
    public partial class TaoTieRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField]
        CameraBufferSettings cameraBuffer = new CameraBufferSettings {
            allowHDR = true,
            renderScale = 1f,
            fxaa = new CameraBufferSettings.FXAA 
            {
                fixedThreshold = 0.0833f,
                relativeThreshold = 0.166f,
                subpixelBlending = 0.75f,
            }
        };
        [SerializeField]
        bool useSRPBatcher = true, useLightsPerObject = true;
        
        [SerializeField]
        ShadowSettings shadows = default;
        
        [SerializeField]
        PostFXSettings postFXSettings = default;
        
        public enum ColorLUTResolution { _16 = 16, _32 = 32, _64 = 64 }

        [SerializeField]
        ColorLUTResolution colorLUTResolution = ColorLUTResolution._32;
        
        [SerializeField]
        Shader cameraRendererShader = default;
        
        [Header("Deprecated Settings")]
        [SerializeField, Tooltip("Dynamic batching is no longer used.")]
        bool useDynamicBatching;

        [SerializeField, Tooltip("GPU instancing is always enabled.")]
        bool useGPUInstancing;
        
        protected override RenderPipeline CreatePipeline()
        {
            return new TaoTieRenderPipeline(cameraBuffer, useSRPBatcher,useLightsPerObject, 
                shadows, postFXSettings, (int)colorLUTResolution,cameraRendererShader);
        }

    }
}