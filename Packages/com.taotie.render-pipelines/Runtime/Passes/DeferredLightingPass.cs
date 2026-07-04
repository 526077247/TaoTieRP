using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class DeferredLightingPass
    {
        static readonly ProfilingSampler sampler = new("Deferred Lighting");

        static readonly int
            gBufferAlbedoAOId = Shader.PropertyToID("_GBufferAlbedoAO"),
            gBufferNormalMSId = Shader.PropertyToID("_GBufferNormalMS"),
            gBufferEmissionId = Shader.PropertyToID("_GBufferEmission"),
            cameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture"),
            inverseViewProjId = Shader.PropertyToID("_InverseViewProj");

        static Material deferredLightingMaterial;

        Material material;
        CameraRendererCopier copier;
        TextureHandle colorAttachment;
        TextureHandle depthAttachment;
        TextureHandle gBufferAlbedoAO, gBufferNormalMS, gBufferEmission;

        void Render(RenderGraphContext context)
        {
            CommandBuffer buffer = context.cmd;
            buffer.SetGlobalTexture(gBufferAlbedoAOId, gBufferAlbedoAO);
            buffer.SetGlobalTexture(gBufferNormalMSId, gBufferNormalMS);
            buffer.SetGlobalTexture(gBufferEmissionId, gBufferEmission);
            buffer.SetGlobalTexture(cameraDepthTextureId, depthAttachment);

            // Set inverse view-projection for world position reconstruction.
            // Use renderIntoTexture:false to match the projection used by SetupCameraProperties
            // when rendering the G-Buffer depth. On D3D this includes the Y-flip that maps
            // texture-convention UVs (Y down) to world space correctly.
            Camera cam = copier.Camera;
            Matrix4x4 view = cam.worldToCameraMatrix;
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(cam.projectionMatrix, false);
            Matrix4x4 invViewProj = (gpuProj * view).inverse;
            buffer.SetGlobalMatrix(inverseViewProjId, invViewProj);

            buffer.SetRenderTarget(
                colorAttachment,
                RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            buffer.SetViewport(new Rect(0, 0, copier.Camera.pixelWidth, copier.Camera.pixelHeight));
            buffer.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, material, 0, 0);
            buffer.SetViewProjectionMatrices(
                copier.Camera.worldToCameraMatrix, cam.projectionMatrix);

            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            CameraRendererCopier copier,
            in CameraRendererTextures textures,
            in GBufferTextures gBuffer,
            in ShadowTextures shadowTextures,
            Shader deferredLightingShader)
        {
            if (deferredLightingMaterial == null || deferredLightingMaterial.shader != deferredLightingShader)
            {
                if (deferredLightingMaterial != null) CoreUtils.Destroy(deferredLightingMaterial);
                deferredLightingMaterial = CoreUtils.CreateEngineMaterial(deferredLightingShader);
            }

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out DeferredLightingPass pass, sampler);

            pass.material = deferredLightingMaterial;
            pass.copier = copier;
            pass.colorAttachment = builder.WriteTexture(textures.colorAttachment);
            pass.depthAttachment = builder.ReadTexture(gBuffer.depthAttachment);
            pass.gBufferAlbedoAO = builder.ReadTexture(gBuffer.albedoAO);
            pass.gBufferNormalMS = builder.ReadTexture(gBuffer.normalMetallicSmoothness);
            pass.gBufferEmission = builder.ReadTexture(gBuffer.emission);

            // Declare read dependency on shadow atlases so RenderGraph keeps them alive
            // — the deferred lighting shader samples them for shadow attenuation.
            builder.ReadTexture(shadowTextures.directionalAtlas);
            builder.ReadTexture(shadowTextures.otherAtlas);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<DeferredLightingPass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Dispose()
        {
            if (deferredLightingMaterial != null)
            {
                CoreUtils.Destroy(deferredLightingMaterial);
                deferredLightingMaterial = null;
            }
        }
    }
}
