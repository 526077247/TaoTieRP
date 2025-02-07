using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;

namespace TaoTie
{
    public partial class TaoTieRenderPipeline: RenderPipeline 
    {
        readonly bool useLightsPerObject;
        ShadowSettings shadowSettings;
        PostFXSettings postFXSettings;
        CameraBufferSettings cameraBufferSettings;
        int colorLUTResolution;
        
        readonly RenderGraph renderGraph = new("TaoTie SRP Render Graph");
        public TaoTieRenderPipeline(CameraBufferSettings cameraBufferSettings, bool useSRPBatcher, 
            bool useLightsPerObject, ShadowSettings shadowSettings,
            PostFXSettings postFXSettings, int colorLUTResolution, Shader cameraRendererShader)
        {
            this.colorLUTResolution = colorLUTResolution;
            this.cameraBufferSettings = cameraBufferSettings;
            this.shadowSettings = shadowSettings;
            this.postFXSettings = postFXSettings;
            this.useLightsPerObject = useLightsPerObject;
            GraphicsSettings.useScriptableRenderPipelineBatching = useSRPBatcher;
            GraphicsSettings.lightsUseLinearIntensity = true;
            InitializeForEditor();
            renderer = new CameraRenderer(cameraRendererShader);
        }

        private CameraRenderer renderer;
        
        protected override void Render(ScriptableRenderContext context, Camera[] cameras) { }

        protected override void Render(ScriptableRenderContext context, List<Camera> cameras)
        {
            base.Render(context, cameras);
            
            for (int i = 0; i < cameras.Count; i++) {
                renderer.Render(renderGraph, context, cameras[i],cameraBufferSettings,useLightsPerObject,
                    shadowSettings, postFXSettings, colorLUTResolution);
            }
            
            renderGraph.EndFrame();
        }
        
        protected override void Dispose (bool disposing) {
            base.Dispose(disposing);
            DisposeForEditor();
            renderer.Dispose();
            renderGraph.Cleanup();
        }
    }
}