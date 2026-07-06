using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class TAAResolvePass
    {
        static readonly ProfilingSampler sampler = new("TAA Resolve");

        static readonly int
            taaCurrentColorID = Shader.PropertyToID("_TAACurrentColor"),
            taaHistoryColorID = Shader.PropertyToID("_TAAHistoryColor"),
            taaTexelSizeID = Shader.PropertyToID("_TAATexelSize"),
            taaBlendFactorID = Shader.PropertyToID("_TAABlendFactor"),
            taaAntiFlickerID = Shader.PropertyToID("_TAAAntiFlicker"),
            taaJitterID = Shader.PropertyToID("_TAAJitter"),
            inverseNonJitteredViewProjID = Shader.PropertyToID("_InverseNonJitteredViewProj"),
            prevViewProjID = Shader.PropertyToID("_PrevViewProj");

        static Material taaMaterial;

        TextureHandle currentColor;
        TextureHandle historyColor;
        TextureHandle depthTexture;
        TextureHandle tempResult;
        TextureHandle colorAttachment;
        TextureHandle historyOutput;
        Vector2Int bufferSize;
        float blendFactor;
        float antiFlicker;
        Vector2 jitter;
        Matrix4x4 inverseNonJitteredViewProj;
        Matrix4x4 prevViewProj;
        Matrix4x4 cameraView;
        Matrix4x4 cameraProj;
        GraphicsFormat colorFormat;

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;

            if (taaMaterial == null)
            {
                var shader = Shader.Find("Hidden/TaoTie RP/TAA");
                if (shader == null)
                {
                    Debug.LogError("Hidden/TaoTie RP/TAA shader not found!");
                    return;
                }
                taaMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            cmd.SetGlobalTexture(taaCurrentColorID, currentColor);
            cmd.SetGlobalTexture(taaHistoryColorID, historyColor);
            cmd.SetGlobalTexture("_CameraDepthTexture", depthTexture);
            cmd.SetGlobalVector(taaTexelSizeID, new Vector4(
                1f / bufferSize.x, 1f / bufferSize.y, bufferSize.x, bufferSize.y));
            cmd.SetGlobalFloat(taaBlendFactorID, blendFactor);
            cmd.SetGlobalFloat(taaAntiFlickerID, antiFlicker);
            cmd.SetGlobalVector(taaJitterID, jitter);
            cmd.SetGlobalMatrix(inverseNonJitteredViewProjID, inverseNonJitteredViewProj);
            cmd.SetGlobalMatrix(prevViewProjID, prevViewProj);

            // Resolve into temp texture (avoid read-write hazard on colorAttachment)
            cmd.SetRenderTarget(
                tempResult,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, taaMaterial, 0, 0);
            cmd.SetViewProjectionMatrices(cameraView, cameraProj);

            // Copy result to color attachment for PostFX/Final
            cmd.CopyTexture(tempResult, colorAttachment);

            // Copy result to history for next frame
            cmd.CopyTexture(tempResult, historyOutput);

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            in CameraRendererTextures textures,
            TextureHandle historyTexture,
            Vector2Int bufferSize,
            float blendFactor,
            float antiFlicker,
            Vector2 jitter,
            Matrix4x4 inverseNonJitteredViewProj,
            Matrix4x4 prevViewProj,
            Camera camera,
            bool useHDR)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out TAAResolvePass pass, sampler);

            TextureHandle sourceColor = textures.resolvedColorAttachment.IsValid()
                ? textures.resolvedColorAttachment
                : textures.colorAttachment;

            pass.colorFormat = useHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            pass.currentColor = builder.ReadTexture(sourceColor);
            pass.historyColor = builder.ReadTexture(historyTexture);
            pass.depthTexture = builder.ReadTexture(textures.depthCopy.IsValid()
                ? textures.depthCopy
                : textures.depthAttachment);
            pass.colorAttachment = builder.WriteTexture(textures.colorAttachment);
            pass.historyOutput = builder.WriteTexture(historyTexture);

            var desc = new TextureDesc(bufferSize.x, bufferSize.y)
            {
                colorFormat = pass.colorFormat,
                name = "TAA Temp Result"
            };
            pass.tempResult = builder.WriteTexture(renderGraph.CreateTexture(desc));

            pass.bufferSize = bufferSize;
            pass.blendFactor = blendFactor;
            pass.antiFlicker = antiFlicker;
            pass.jitter = jitter;
            pass.inverseNonJitteredViewProj = inverseNonJitteredViewProj;
            pass.prevViewProj = prevViewProj;
            pass.cameraView = camera.worldToCameraMatrix;
            pass.cameraProj = camera.projectionMatrix;

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<TAAResolvePass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Dispose()
        {
            if (taaMaterial != null)
            {
                Object.DestroyImmediate(taaMaterial);
                taaMaterial = null;
            }
        }
    }
}
