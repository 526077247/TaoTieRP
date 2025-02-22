﻿using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class DebugPass
    {
        static readonly ProfilingSampler sampler = new("Debug");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph,
            TaoTieRenderPipelineSettings settings,
            Camera camera)
        {
            if (CameraDebugger.IsActive &&
                camera.cameraType <= CameraType.SceneView)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    sampler.name, out DebugPass pass, sampler);
                builder.SetRenderFunc<DebugPass>(
                    static (pass, context) => CameraDebugger.Render(context));
            }
        }
    }
}