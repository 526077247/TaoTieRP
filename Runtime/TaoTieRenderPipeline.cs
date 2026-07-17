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
                settings.overdrawResolveShader,
                settings.taaShader,
#if !UNITY_WEBGL || UNITY_EDITOR
                settings.forwardPlusCullCompute);
#else
                null);
#endif

            LensFlarePass.Initialize();

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
            // Keyword is toggled per-camera in CameraRenderer based on actual light count.
            // Set initial state to disabled; CameraRenderer will enable as needed.
            Shader.DisableKeyword("_TAOTIE_FORWARD_PLUS");
        }

        private CameraRenderer renderer;

        static CameraSettings defaultCameraSettings = new CameraSettings();

        static CameraSettings GetCameraSettings(Camera camera)
        {
            if (camera.TryGetComponent(out TaoTieRenderPipelineCamera crpCamera))
                return crpCamera.Settings;
            return defaultCameraSettings;
        }
        
        void RenderCameraStack(ScriptableRenderContext context, Camera baseCamera)
        {
            CameraSettings baseSettings = GetCameraSettings(baseCamera);
            if (baseSettings.renderingMode == CameraSettings.RenderingMode.RenderDirectToScreen)
                renderer.RenderOverlayDirect(context, baseCamera, settings);
            else
                renderer.Render(renderGraph, context, baseCamera, settings);
        }

        protected override void Render(ScriptableRenderContext context, Camera[] cameras)
        {
            RenderForEditor();
            for (int i = 0; i < cameras.Length; i++)
            {
                RenderCameraStack(context, cameras[i]);
            }

            renderGraph.EndFrame();
        }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            RenderForEditor();
            for (int i = 0; i < cameras.Count; i++)
            {
                RenderCameraStack(context, cameras[i]);
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