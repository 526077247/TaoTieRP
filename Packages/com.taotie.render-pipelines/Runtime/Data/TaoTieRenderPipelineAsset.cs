using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [CreateAssetMenu(menuName = "Rendering/TaoTie Pipeline")]
    public partial class TaoTieRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] public TaoTieRenderPipelineSettings settings = new TaoTieRenderPipelineSettings();

        protected override RenderPipeline CreatePipeline()
        {
            if (settings.cameraDebuggerShader == null)
            {
                settings.cameraDebuggerShader = Shader.Find("Hidden/TaoTie RP/Camera Debugger");
                if (settings.cameraDebuggerShader == null)
                {
                    Debug.LogError("Hidden/TaoTie RP/Camera Debugger shader not found!");
                }
            }
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