using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] public TaoTieRenderPipelineSettings settings = new TaoTieRenderPipelineSettings();

        protected override RenderPipeline CreatePipeline()
        {
            if (settings.cameraRendererShader == null)
            {
                settings.cameraRendererShader = Shader.Find("Hidden/TaoTie RP/Camera Renderer");
                if (settings.cameraRendererShader == null)
                {
                    Debug.LogError("Hidden/TaoTie RP/Camera Renderer shader not found!");
                }
            }
            return new TaoTieRenderPipeline(settings);
        }

    }
}