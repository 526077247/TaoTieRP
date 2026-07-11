using System.Diagnostics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;

namespace TaoTie.RenderPipelines
{
    public class OverdrawDebuggerPass
    {
        static readonly ProfilingSampler sampler = new("Overdraw Debugger");

        static readonly int opacityID = Shader.PropertyToID("_OverdrawOpacity");
        static readonly int counterTexID = Shader.PropertyToID("_OverdrawCounterTex");

        static readonly ShaderTagId[] overdrawShaderTagIDs = {
            new("CustomLit"),
            new("DepthOnly"),
        };

        static Material overdrawMaterial;
        static Material resolveMaterial;
        static RTHandle counterRT;
        static int width, height;
        static bool isHDR;

        TextureHandle counterTexture;
        RendererListHandle list;

        void RenderClear(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetRenderTarget(counterTexture);
            buffer.ClearRenderTarget(false, true, Color.clear);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        void RenderOverdraw(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetRenderTarget(counterTexture);
            buffer.DrawRendererList(list);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        void RenderResolve(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetGlobalFloat(opacityID, 1f);
            buffer.SetGlobalTexture(counterTexID, counterTexture);
            buffer.SetRenderTarget(
                BuiltinRenderTextureType.CameraTarget,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            buffer.DrawProcedural(
                Matrix4x4.identity, resolveMaterial, 0, MeshTopology.Triangles, 3);
            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }
        public static bool IsActive => OverdrawDebugger.IsActive;

        public static void Initialize(Shader resolveShader)
        {
            if (resolveMaterial == null)
                resolveMaterial = CoreUtils.CreateEngineMaterial(resolveShader);
            // Overdraw material uses a simple unlit additive shader
            var overdrawShader = Shader.Find("Hidden/TaoTie RP/Overdraw");
            if (overdrawShader != null && overdrawMaterial == null)
                overdrawMaterial = new Material(overdrawShader);
        }

        public static void Cleanup()
        {
            CoreUtils.Destroy(resolveMaterial);
            CoreUtils.Destroy(overdrawMaterial);
            if (counterRT != null)
            {
                counterRT.Release();
                counterRT = null;
            }
        }

        public static void EnsureCounterTexture(int w, int h, bool hdr)
        {
            if (counterRT != null && width == w && height == h && isHDR == hdr)
                return;

            if (counterRT != null)
                counterRT.Release();

            var desc = new RenderTextureDescriptor(w, h,
                hdr ? RenderTextureFormat.ARGBHalf : RenderTextureFormat.ARGB32, 0, 1);
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            var rt = new RenderTexture(desc);
            rt.filterMode = FilterMode.Point;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            counterRT = RTHandles.Alloc(rt, "Overdraw Counter");
            width = w;
            height = h;
            isHDR = hdr;
        }

        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        public static void Record(
            RenderGraph renderGraph,
            Camera camera,
            CullingResults cullingResults,
            int renderingLayerMask,
            in CameraRendererTextures textures,
            Vector2Int bufferSize, bool useHDR)
        {
            if (!OverdrawDebugger.IsActive || overdrawMaterial == null) return;

            EnsureCounterTexture(bufferSize.x, bufferSize.y, useHDR);
            var importedCounter = renderGraph.ImportTexture(counterRT);

            // Pass 1: Clear counter
            using RenderGraphBuilder clearBuilder = renderGraph.AddRenderPass(
                "Overdraw Clear", out OverdrawDebuggerPass clearPass, sampler);
            clearPass.counterTexture = clearBuilder.WriteTexture(importedCounter);
            clearBuilder.AllowPassCulling(false);
            clearBuilder.SetRenderFunc<OverdrawDebuggerPass>(
                static (pass, context) => pass.RenderClear(context));

            // Pass 2: Render geometry with overdraw material into counter (additive)
            using RenderGraphBuilder overdrawBuilder = renderGraph.AddRenderPass(
                "Overdraw Geometry", out OverdrawDebuggerPass overdrawPass, sampler);
            overdrawPass.counterTexture = overdrawBuilder.WriteTexture(importedCounter);
            overdrawPass.list = overdrawBuilder.UseRendererList(renderGraph.CreateRendererList(
                new RendererListDesc(overdrawShaderTagIDs, cullingResults, camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.opaque,
                    renderingLayerMask = (uint)renderingLayerMask,
                    overrideMaterial = overdrawMaterial,
                    overrideMaterialPassIndex = 0
                }));
            overdrawBuilder.AllowPassCulling(false);
            overdrawBuilder.SetRenderFunc<OverdrawDebuggerPass>(
                static (pass, context) => pass.RenderOverdraw(context));

            // Pass 3: Resolve counter to screen overlay
            using RenderGraphBuilder resolveBuilder = renderGraph.AddRenderPass(
                "Overdraw Resolve", out OverdrawDebuggerPass resolvePass, sampler);
            resolvePass.counterTexture = resolveBuilder.ReadTexture(importedCounter);
            resolveBuilder.AllowPassCulling(false);
            resolveBuilder.SetRenderFunc<OverdrawDebuggerPass>(
                static (pass, context) => pass.RenderResolve(context));
        }
    }
}
