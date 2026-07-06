using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie.RenderPipelines
{
    public partial class TaoTieRenderPipeline : RenderPipeline
    {
        readonly TaoTieRenderPipelineSettings settings;

        readonly RenderGraph renderGraph = new("TaoTie SRP Render Graph");

        public TaoTieRenderPipeline(TaoTieRenderPipelineSettings settings)
        {
            this.settings = settings;
            GraphicsSettings.useScriptableRenderPipelineBatching =
                settings.useSRPBatcher;
            GraphicsSettings.lightsUseLinearIntensity = true;

            // WebGL does not support ComputeBuffer; use Texture2D fallback there.
            if (SystemInfo.supportsComputeShaders)
                Shader.EnableKeyword("_COMPUTE_BUFFER");
            else
                Shader.DisableKeyword("_COMPUTE_BUFFER");

            UpdateForwardPlusKeyword();

            InitializeForEditor();
            var deferredShader = Shader.Find("Hidden/TaoTie RP/Deferred Lighting");
            if (deferredShader == null)
                Debug.LogWarning("Hidden/TaoTie RP/Deferred Lighting shader not found — Deferred path will fall back to Forward.");
            renderer = new(
                settings.cameraRendererShader,
                deferredShader);
        }

        void UpdateForwardPlusKeyword()
        {
            bool useForwardPlus = settings.shadows.useForwardPlus &&
                                  SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;
            if (useForwardPlus)
                Shader.EnableKeyword("_TAOTIE_FORWARD_PLUS");
            else
                Shader.DisableKeyword("_TAOTIE_FORWARD_PLUS");
        }

        private CameraRenderer renderer;

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            Debug.LogError("Using Render List<Camera>");
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            RenderForEditor();
            for (int i = 0; i < cameras.Count; i++)
            {
                renderer.Render(renderGraph, context, cameras[i], settings);
            }

            renderGraph.EndFrame();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            DisposeForEditor();
            renderer.Dispose();
            renderGraph.Cleanup();
        }
    }
}