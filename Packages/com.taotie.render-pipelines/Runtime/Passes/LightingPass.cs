using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie
{
    public class LightingPass
    {
        Lighting lighting;

        CullingResults cullingResults;

        ShadowSettings shadowSettings;

        bool useLightsPerObject;

        int renderingLayerMask;

        void Render(RenderGraphContext context) => lighting.Setup(
            context, cullingResults, shadowSettings,
            useLightsPerObject, renderingLayerMask);

        public static void Record(
            RenderGraph renderGraph, Lighting lighting,
            CullingResults cullingResults, ShadowSettings shadowSettings,
            bool useLightsPerObject, int renderingLayerMask)
        {
            using RenderGraphBuilder builder =
                renderGraph.AddRenderPass("Lighting", out LightingPass pass);
            pass.lighting = lighting;
            pass.cullingResults = cullingResults;
            pass.shadowSettings = shadowSettings;
            pass.useLightsPerObject = useLightsPerObject;
            pass.renderingLayerMask = renderingLayerMask;
            builder.SetRenderFunc<LightingPass>((pass, context) => pass.Render(context));
        }
    }
}