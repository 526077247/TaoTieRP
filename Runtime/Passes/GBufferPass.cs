using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
    public class GBufferPass
    {
        static readonly ProfilingSampler sampler = new("G-Buffer");

        static readonly ShaderTagId[] shaderTagIDs =
        {
            new("SRPDefaultUnlit"),
            new("DeferredGBuffer")
        };

        static readonly RenderTargetIdentifier[] colorTargets = new RenderTargetIdentifier[3];

        // Cache format support to avoid repeated SystemInfo queries.
        static readonly bool hdrFloatSupported =
            SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render);
        static readonly GraphicsFormat albedoLDRFormat = GraphicsFormat.R8G8B8A8_UNorm;
        static readonly GraphicsFormat normalFormat = GraphicsFormat.R8G8B8A8_UNorm;

        RendererListHandle list;
        TextureHandle albedoAO, normalMS, emission, depthAttachment;

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;
            colorTargets[0] = albedoAO;
            colorTargets[1] = normalMS;
            colorTargets[2] = emission;
            cmd.SetRenderTarget(colorTargets, depthAttachment);
            cmd.ClearRenderTarget(true, true, Color.clear);
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();

            cmd.DrawRendererList(list);
            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static GBufferTextures Record(
            RenderGraph renderGraph,
            Camera camera,
            CullingResults cullingResults,
            int renderingLayerMask,
            bool useHDR,
            Vector2Int bufferSize,
            TextureHandle depthAttachment,
            in ShadowTextures shadowTextures)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out GBufferPass pass, sampler);

            pass.list = builder.UseRendererList(renderGraph.CreateRendererList(
                new RendererListDesc(shaderTagIDs, cullingResults, camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    rendererConfiguration =
                        PerObjectData.ReflectionProbes |
                        PerObjectData.Lightmaps |
                        PerObjectData.ShadowMask |
                        PerObjectData.LightProbe |
                        PerObjectData.OcclusionProbe |
                        PerObjectData.LightProbeProxyVolume |
                        PerObjectData.OcclusionProbeProxyVolume,
                    renderQueueRange = RenderQueueRange.opaque,
                    renderingLayerMask = (uint)renderingLayerMask
                }));

            // Select formats with runtime fallback for WebGL / mobile compatibility.
            // Normals always use UNorm since encoding remaps to [0,1].
            GraphicsFormat albedoFormat = useHDR && hdrFloatSupported
                ? GraphicsFormat.R16G16B16A16_SFloat
                : albedoLDRFormat;
            GraphicsFormat normalFmt = (useHDR && hdrFloatSupported)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : normalFormat;

            var desc = new TextureDesc(bufferSize.x, bufferSize.y)
            {
                colorFormat = albedoFormat,
                name = "G-Buffer Albedo AO"
            };
            pass.albedoAO = builder.WriteTexture(renderGraph.CreateTexture(desc));

            desc.colorFormat = normalFmt;
            desc.name = "G-Buffer Normal Metallic";
            pass.normalMS = builder.WriteTexture(renderGraph.CreateTexture(desc));

            desc.colorFormat = GraphicsFormat.R32G32B32A32_SFloat;
            desc.name = "G-Buffer Emission";
            pass.emission = builder.WriteTexture(renderGraph.CreateTexture(desc));

            pass.depthAttachment = builder.WriteTexture(depthAttachment);

            builder.ReadTexture(shadowTextures.directionalAtlas);
            builder.ReadTexture(shadowTextures.otherAtlas);

            builder.AllowPassCulling(true);
            builder.SetRenderFunc<GBufferPass>(
                static (pass, context) => pass.Render(context));

            return new GBufferTextures(
                pass.albedoAO, pass.normalMS, pass.emission, depthAttachment);
        }
    }
}
