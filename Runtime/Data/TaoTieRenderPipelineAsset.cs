using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] public TaoTieRenderPipelineSettings settings = new TaoTieRenderPipelineSettings();

        protected override RenderPipeline CreatePipeline()
        {
            EnsureShader(ref settings.cameraRendererShader, "Hidden/TaoTie RP/Camera Renderer");
            EnsureShader(ref settings.deferredLightingShader, "Hidden/TaoTie RP/Deferred Lighting");
            EnsureShader(ref settings.taaShader, "Hidden/TaoTie RP/TAA");
            EnsureShader(ref settings.forwardPlusDebuggerShader, "Hidden/TaoTie RP/ForwardPlus Debugger");
            EnsureShader(ref settings.depthDebuggerShader, "Hidden/TaoTie RP/Depth Debugger");
            return new TaoTieRenderPipeline(settings);
        }

        static void EnsureShader(ref Shader field, string name)
        {
            if (field == null)
                field = Shader.Find(name);
            if (field == null)
                Debug.LogError($"{name} shader not found!");
        }

    }
}