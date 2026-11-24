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
        // TAA jittered projection used by SetupPass / GBufferPass. Must be restored after
        // this fullscreen pass so transparent geometry (roads drawn slightly above terrain)
        // keeps the SAME projection as the depth buffer written by the G-Buffer.
        // Restoring the non-jittered camera.projectionMatrix here makes transparent objects
        // z-fight against jittered G-Buffer depth, flickering every TAA frame.
        Matrix4x4 jitteredProjection;

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
            // Restore the SAME jittered projection SetupPass applied, not the raw camera
            // projection. Otherwise transparent geometry rendered after this pass (e.g. the
            // hex road, offset +0.04 above terrain) is transformed with a different projection
            // than the G-Buffer depth it is tested against -> per-frame flicker under TAA.
            buffer.SetViewProjectionMatrices(
                copier.Camera.worldToCameraMatrix, jitteredProjection);

            context.renderContext.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            CameraRendererCopier copier,
            in CameraRendererTextures textures,
            in GBufferTextures gBuffer,
            in ShadowTextures shadowTextures,
            Shader deferredLightingShader,
            Matrix4x4 jitteredProjection)
        {
            if (deferredLightingShader == null) return;

            if (deferredLightingMaterial == null || deferredLightingMaterial.shader != deferredLightingShader)
            {
                if (deferredLightingMaterial != null) CoreUtils.Destroy(deferredLightingMaterial);
                deferredLightingMaterial = CoreUtils.CreateEngineMaterial(deferredLightingShader);
            }

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out DeferredLightingPass pass, sampler);

            pass.material = deferredLightingMaterial;
            pass.copier = copier;
            pass.jitteredProjection = jitteredProjection;
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
