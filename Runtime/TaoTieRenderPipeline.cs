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

            VolumeManager.instance.CheckBaseTypes();

            UpdateForwardPlusKeyword();

            InitializeForEditor();
            renderer = new(
                settings.cameraRendererShader,
#if !UNITY_WEBGL || UNITY_EDITOR
                settings.deferredLightingShader,
#else
                null,
#endif
                settings.forwardPlusDebuggerShader,
                settings.depthDebuggerShader,
                settings.taaShader);

            // Pre-warm PostFX material to avoid first-frame hitch
            if (settings.postFXSettings != null)
            {
                _ = settings.postFXSettings.Material;
                foreach (var effect in settings.postFXSettings.Effects)
                    effect?.EnsureShaderReference();
            }
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