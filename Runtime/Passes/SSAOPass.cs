using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class SSAOPass
    {
        static readonly ProfilingSampler sampler = new("SSAO");

        static Material ssaoMaterial;
        static readonly int
            texelSizeID = Shader.PropertyToID("_SSAOTexelSize"),
            paramsID = Shader.PropertyToID("_SSAOParams"),
            inverseProjID = Shader.PropertyToID("_SSAOInverseProj"),
            projID = Shader.PropertyToID("_SSAOProj"),
            sourceID = Shader.PropertyToID("_SSAOSource"),
            ssaoTexID = Shader.PropertyToID("_ScreenSpaceOcclusionTexture"),
            cameraDepthTextureID = Shader.PropertyToID("_CameraDepthTexture");

        TextureHandle depthTexture;
        TextureHandle ssaoTemp;
        TextureHandle ssaoResult;
        Vector2Int ssaoSize;
        SSAOSettings settings;
        Matrix4x4 inverseProj;
        Matrix4x4 proj;
        Matrix4x4 cameraView;
        Matrix4x4 cameraProj;
        TextureHandle rtColor;
        TextureHandle rtDepth;

        static Shader cachedShader;

        static void EnsureMaterial()
        {
            if (cachedShader == null)
                cachedShader = Shader.Find("Hidden/TaoTie RP/SSAO");
            if (cachedShader == null)
            {
                ssaoMaterial = null;
                return;
            }
            if (ssaoMaterial == null || ssaoMaterial.shader != cachedShader)
            {
                if (cachedShader.passCount < 3) { ssaoMaterial = null; return; }
                if (ssaoMaterial != null) Object.DestroyImmediate(ssaoMaterial);
                ssaoMaterial = new Material(cachedShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        void Render(RenderGraphContext context)
        {
            EnsureMaterial();
            if (ssaoMaterial == null) return;

            CommandBuffer cmd = context.cmd;

            // Set parameters
            cmd.SetGlobalVector(texelSizeID, new Vector4(
                1f / ssaoSize.x, 1f / ssaoSize.y, ssaoSize.x, ssaoSize.y));
            cmd.SetGlobalVector(paramsID, new Vector4(
                settings.intensity, settings.radius, settings.falloff, settings.downsample));
            cmd.SetGlobalMatrix(inverseProjID, inverseProj);
            cmd.SetGlobalMatrix(projID, proj);

            // Pass 0: Generate SSAO
            cmd.SetGlobalTexture(cameraDepthTextureID, depthTexture);
            cmd.SetRenderTarget(ssaoTemp,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.SetViewport(new Rect(0, 0, ssaoSize.x, ssaoSize.y));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ssaoMaterial, 0, 0);

            // Pass 1: Horizontal blur (temp -> result)
            cmd.SetGlobalTexture(sourceID, ssaoTemp);
            cmd.SetRenderTarget(ssaoResult,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ssaoMaterial, 0, 1);

            // Pass 2: Vertical blur (result -> temp)
            cmd.SetGlobalTexture(sourceID, ssaoResult);
            cmd.SetRenderTarget(ssaoTemp,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, ssaoMaterial, 0, 2);

            // Set global for lighting shaders (read from temp, the final blur output)
            cmd.SetGlobalTexture(ssaoTexID, ssaoTemp);

            // Restore render target for subsequent passes (transparent geometry, etc.)
            cmd.SetRenderTarget(
                rtColor,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                rtDepth,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

            // Restore VP
            cmd.SetViewProjectionMatrices(cameraView, cameraProj);

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static TextureHandle Record(
            RenderGraph renderGraph,
            in CameraRendererTextures textures,
            Vector2Int bufferSize,
            SSAOSettings settings,
            Camera camera)
        {
            EnsureMaterial();
            if (ssaoMaterial == null || !settings.enabled)
                return default;

            int ssaoW = Mathf.Max(1, Mathf.CeilToInt(bufferSize.x * settings.downsample));
            int ssaoH = Mathf.Max(1, Mathf.CeilToInt(bufferSize.y * settings.downsample));
            Vector2Int ssaoSize = new(ssaoW, ssaoH);

            // Enable keyword on material
            string kw = settings.sampleCount switch
            {
                SSAOSettings.SampleCount.High => "SSAO_HIGH",
                SSAOSettings.SampleCount.Medium => "SSAO_MEDIUM",
                _ => "SSAO_LOW"
            };
            ssaoMaterial.DisableKeyword("SSAO_HIGH");
            ssaoMaterial.DisableKeyword("SSAO_MEDIUM");
            ssaoMaterial.DisableKeyword("SSAO_LOW");
            ssaoMaterial.EnableKeyword(kw);

            // Compute projection matrices (non-jittered, view-space SSAO)
            Matrix4x4 proj = camera.projectionMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(proj, true);
            Matrix4x4 invProj = Matrix4x4.Inverse(gpuProj);

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out SSAOPass pass, sampler);

            TextureHandle depthHandle = textures.depthCopy.IsValid()
                ? textures.depthCopy : textures.depthAttachment;
            pass.depthTexture = builder.ReadTexture(depthHandle);

            // Declare color and depth as render targets for restore at end of pass
            pass.rtColor = builder.UseColorBuffer(textures.colorAttachment, 0);
            pass.rtDepth = textures.depthAttachment;

            var format = SystemInfo.IsFormatSupported(GraphicsFormat.R8_UNorm, FormatUsage.Render)
                ? GraphicsFormat.R8_UNorm : GraphicsFormat.R8G8B8A8_UNorm;

            var desc = new TextureDesc(ssaoSize.x, ssaoSize.y)
            {
                colorFormat = format,
                name = "SSAO Temp"
            };
            pass.ssaoTemp = builder.WriteTexture(renderGraph.CreateTexture(desc));
            desc.name = "SSAO Result (horizontal blur)";
            pass.ssaoResult = builder.WriteTexture(renderGraph.CreateTexture(desc));

            pass.ssaoSize = ssaoSize;
            pass.settings = settings;
            pass.inverseProj = invProj;
            pass.proj = gpuProj;
            pass.cameraView = camera.worldToCameraMatrix;
            pass.cameraProj = camera.projectionMatrix;
            pass.rtColor = textures.colorAttachment;
            pass.rtDepth = builder.ReadTexture(textures.depthAttachment);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<SSAOPass>(
                static (pass, context) => pass.Render(context));

            return pass.ssaoResult;
        }

        public static void Dispose()
        {
            if (ssaoMaterial != null)
            {
                Object.DestroyImmediate(ssaoMaterial);
                ssaoMaterial = null;
            }
        }
    }
}
