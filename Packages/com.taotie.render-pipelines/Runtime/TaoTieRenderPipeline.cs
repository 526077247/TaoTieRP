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

            bool useForwardPlus = settings.shadows.renderingMode switch
            {
                ShadowSettings.RenderingMode.Auto =>
                    SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2,
                ShadowSettings.RenderingMode.ForwardPlus => true,
                ShadowSettings.RenderingMode.Forward => false,
                _ => true
            };
            if (useForwardPlus)
                Shader.EnableKeyword("_TAOTIE_FORWARD_PLUS");
            else
                Shader.DisableKeyword("_TAOTIE_FORWARD_PLUS");

            // WebGL does not support ComputeBuffer; use Texture2D fallback there.
            if (SystemInfo.supportsComputeShaders)
                Shader.EnableKeyword("_COMPUTE_BUFFER");
            else
                Shader.DisableKeyword("_COMPUTE_BUFFER");

            InitializeForEditor();
            renderer = new(settings.cameraRendererShader, settings.cameraDebuggerShader);
        }

        private CameraRenderer renderer;

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            Debug.LogError("Using Render List<Camera>");
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
#if UNITY_EDITOR
            if (UnityEditor.BuildPipeline.isBuildingPlayer)
                return;
#endif

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