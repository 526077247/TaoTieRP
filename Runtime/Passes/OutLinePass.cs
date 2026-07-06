using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class OutLinePass
    {
        static readonly ProfilingSampler samplerOutLine = new("OutLine");

        static readonly int
            outlineSourceID = Shader.PropertyToID("_OutlineSource"),
            cameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture"),
            gBufferNormalMSID = Shader.PropertyToID("_GBufferNormalMS"),
            outlineColorID = Shader.PropertyToID("_OutlineColor"),
            outlineDepthSensitivityID = Shader.PropertyToID("_OutlineDepthSensitivity"),
            outlineNormalSensitivityID = Shader.PropertyToID("_OutlineNormalSensitivity"),
            outlineWidthID = Shader.PropertyToID("_OutlineWidth");

        static Material outlineMaterial;

        TextureHandle colorSource;
        TextureHandle depthTexture;
        TextureHandle gBufferNormalMS;
        TextureHandle tempResult;
        TextureHandle colorAttachment;
        Camera camera;
        Vector2Int bufferSize;
        Color outlineColor;
        float outlineDepthSensitivity;
        float outlineNormalSensitivity;
        float outlineWidth;
        bool useGBufferNormals;

        void Render(RenderGraphContext context)
        {
            CommandBuffer cmd = context.cmd;

            if (outlineMaterial == null)
            {
                Shader shader = Shader.Find("Hidden/TaoTie RP/Outline");
                if (shader == null)
                {
                    Debug.LogError("Hidden/TaoTie RP/Outline shader not found!");
                    return;
                }
                outlineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }

            // Toggle keyword: G-Buffer normals (deferred) vs depth-only (forward)
            if (useGBufferNormals)
                outlineMaterial.EnableKeyword("_OUTLINE_USE_GBUFFER_NORMALS");
            else
                outlineMaterial.DisableKeyword("_OUTLINE_USE_GBUFFER_NORMALS");

            // Bind source textures
            // _CameraDepthTexture is already set globally by DepthPrePass or CopyAttachmentsPass
            cmd.SetGlobalTexture(outlineSourceID, colorSource);
            if (useGBufferNormals && gBufferNormalMS.IsValid())
                cmd.SetGlobalTexture(gBufferNormalMSID, gBufferNormalMS);

            // Set outline parameters
            cmd.SetGlobalVector(outlineColorID, outlineColor);
            cmd.SetGlobalFloat(outlineDepthSensitivityID, outlineDepthSensitivity);
            cmd.SetGlobalFloat(outlineNormalSensitivityID, outlineNormalSensitivity);
            cmd.SetGlobalFloat(outlineWidthID, outlineWidth);

            // Draw fullscreen quad into temp texture (avoid read-write hazard on colorAttachment)
            cmd.SetRenderTarget(
                tempResult,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, outlineMaterial, 0, 0);

            // Blit result back to color attachment (draw-based, properly tracked by RenderGraph)
            cmd.SetGlobalTexture(outlineSourceID, tempResult);
            cmd.SetRenderTarget(
                colorAttachment,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, outlineMaterial, 0, 1);
            cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            Camera camera,
            in CameraRendererTextures textures,
            in CameraBufferSettings bufferSettings,
            bool useGBufferNormals,
            TextureHandle gBufferNormalMS,
            Vector2Int bufferSize,
            bool useHDR,
            MSAASamples msaaSamples)
        {
            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                samplerOutLine.name, out OutLinePass pass, samplerOutLine);

            pass.colorSource = builder.ReadTexture(textures.resolvedColorAttachment.IsValid()
                ? textures.resolvedColorAttachment
                : textures.colorAttachment);
            pass.depthTexture = builder.ReadTexture(textures.depthAttachment);
            if (useGBufferNormals && gBufferNormalMS.IsValid())
                pass.gBufferNormalMS = builder.ReadTexture(gBufferNormalMS);
            else
                pass.gBufferNormalMS = default;
            pass.colorAttachment = builder.WriteTexture(textures.resolvedColorAttachment.IsValid()
                ? textures.resolvedColorAttachment
                : textures.colorAttachment);

            GraphicsFormat colorFormat = useHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            var desc = new TextureDesc(bufferSize.x, bufferSize.y)
            {
                colorFormat = colorFormat,
                msaaSamples = msaaSamples,
                name = "Outline Temp Result"
            };
            pass.tempResult = builder.WriteTexture(renderGraph.CreateTexture(desc));

            pass.camera = camera;
            pass.bufferSize = bufferSize;
            pass.outlineColor = bufferSettings.outLineColor;
            pass.outlineDepthSensitivity = bufferSettings.outLineDepthSensitivity;
            pass.outlineNormalSensitivity = bufferSettings.outLineNormalSensitivity;
            pass.outlineWidth = bufferSettings.outLineWidth;
            pass.useGBufferNormals = useGBufferNormals;

            builder.AllowPassCulling(bufferSettings.outLineColor.a <= 0f || bufferSettings.outLineWidth <= 0f);
            builder.SetRenderFunc<OutLinePass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Dispose()
        {
            if (outlineMaterial != null)
            {
                CoreUtils.Destroy(outlineMaterial);
                outlineMaterial = null;
            }
        }
    }
}
