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

            UpdateForwardPlusKeyword();

            InitializeForEditor();
            renderer = new(
                settings.cameraRendererShader,
                settings.deferredLightingShader,
                settings.forwardPlusDebuggerShader,
                settings.depthDebuggerShader,
                settings.taaShader,
                settings.outlineShader);
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
            RenderForEditor();
            for (int i = 0; i < cameras.Length; i++)
            {
                renderer.Render(renderGraph, context, cameras[i], settings);
            }

            renderGraph.EndFrame();
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