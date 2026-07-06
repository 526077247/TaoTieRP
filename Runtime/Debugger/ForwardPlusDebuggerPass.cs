using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class ForwardPlusDebuggerPass
    {
        static readonly ProfilingSampler sampler = new("Debug");

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph,
            TaoTieRenderPipelineSettings settings,
            Camera camera)
        {
            if (ForwardPlusDebugger.IsActive &&
                camera.cameraType <= CameraType.SceneView)
            {
                using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                    sampler.name, out ForwardPlusDebuggerPass pass, sampler);
                builder.SetRenderFunc<ForwardPlusDebuggerPass>(
                    static (pass, context) => ForwardPlusDebugger.Render(context));
            }
        }
    }
}