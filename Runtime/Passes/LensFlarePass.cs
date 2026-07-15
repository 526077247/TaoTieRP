using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class LensFlarePass
    {
        static readonly ProfilingSampler sampler = new("Lens Flare");

        static Material flareMaterial;
        static Shader cachedShader;

        // Shader property IDs matching LensFlareCommonSRP expectations
        static readonly int
            flareOcclusionRemapTexID = Shader.PropertyToID("_FlareOcclusionRemapTex"),
            flareOcclusionTexID = Shader.PropertyToID("_FlareOcclusionTex"),
            flareOcclusionIndexID = Shader.PropertyToID("_FlareOcclusionIndex"),
            flareCloudOpacityID = Shader.PropertyToID("_FlareCloudOpacity"),
            flareSunOcclusionTexID = Shader.PropertyToID("_FlareSunOcclusionTex"),
            flareTexID = Shader.PropertyToID("_FlareTex"),
            flareColorValueID = Shader.PropertyToID("_FlareColorValue"),
            flareData0ID = Shader.PropertyToID("_FlareData0"),
            flareData1ID = Shader.PropertyToID("_FlareData1"),
            flareData2ID = Shader.PropertyToID("_FlareData2"),
            flareData3ID = Shader.PropertyToID("_FlareData3"),
            flareData4ID = Shader.PropertyToID("_FlareData4");

        Camera camera;
        Vector2Int bufferSize;
        TextureHandle colorTarget;
        bool taaEnabled;

        static void EnsureMaterial()
        {
            if (cachedShader == null)
                cachedShader = Shader.Find("Hidden/TaoTie RP/Lens Flare");
            if (cachedShader == null)
            {
                flareMaterial = null;
                return;
            }
            if (flareMaterial == null || flareMaterial.shader != cachedShader)
            {
                if (flareMaterial != null) CoreUtils.Destroy(flareMaterial);
                flareMaterial = new Material(cachedShader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        static float GetLensFlareLightAttenuation(Light light, Camera cam, Vector3 wo)
        {
            if (light == null) return 1f;

            return light.type switch
            {
                LightType.Directional => LensFlareCommonSRP.ShapeAttenuationDirLight(light.transform.forward, wo),
                LightType.Spot => LensFlareCommonSRP.ShapeAttenuationSpotConeLight(
                    light.transform.forward, wo, light.spotAngle,
                    light.innerSpotAngle / 90f),
                _ => LensFlareCommonSRP.ShapeAttenuationPointLight(),
            };
        }

        void Render(RenderGraphContext context)
        {
            if (flareMaterial == null) return;
            if (LensFlareCommonSRP.Instance.IsEmpty()) return;

            CommandBuffer cmd = context.cmd;
            Camera cam = camera;
            float actualWidth = bufferSize.x;
            float actualHeight = bufferSize.y;
            Matrix4x4 viewProjMatrix = cam.nonJitteredProjectionMatrix * cam.worldToCameraMatrix;

            // Compute occlusion into occlusionRT (no-op when !IsOcclusionRTCompatible, e.g. GLES2)
            LensFlareCommonSRP.ComputeOcclusion(
                flareMaterial, cam, actualWidth, actualHeight,
                false, 0f, 1f, false,
                cam.transform.position, viewProjMatrix, cmd,
                taaEnabled, false, null, null,
                flareOcclusionTexID, flareCloudOpacityID, flareOcclusionIndexID,
                flareTexID, flareColorValueID, flareSunOcclusionTexID,
                flareData0ID, flareData1ID, flareData2ID, flareData3ID, flareData4ID);

            // Draw lens flares into color target
            LensFlareCommonSRP.DoLensFlareDataDrivenCommon(
                flareMaterial, cam, actualWidth, actualHeight,
                false, 0f, 1f, false,
                cam.transform.position, viewProjMatrix, cmd,
                taaEnabled, false, null, null,
                colorTarget,
                GetLensFlareLightAttenuation,
                flareOcclusionRemapTexID, flareOcclusionTexID, flareOcclusionIndexID,
                flareCloudOpacityID, flareSunOcclusionTexID,
                flareTexID, flareColorValueID,
                flareData0ID, flareData1ID, flareData2ID, flareData3ID, flareData4ID,
                false);

            context.renderContext.ExecuteCommandBuffer(cmd);
            cmd.Clear();
        }

        public static void Record(
            RenderGraph renderGraph,
            Camera camera,
            in CameraRendererTextures textures,
            bool useDepthTexture,
            Vector2Int bufferSize,
            bool useHDR,
            bool taaEnabled)
        {
            if (LensFlareCommonSRP.Instance.IsEmpty()) return;
            if (SystemInfo.graphicsShaderLevel < 35) return;

            EnsureMaterial();
            if (flareMaterial == null) return;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out LensFlarePass pass, sampler);

            pass.camera = camera;
            pass.bufferSize = bufferSize;
            pass.taaEnabled = taaEnabled;
            pass.colorTarget = builder.UseColorBuffer(textures.colorAttachment, 0);

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<LensFlarePass>(
                static (pass, context) => pass.Render(context));
        }

        public static void Initialize()
        {
            LensFlareCommonSRP.Initialize();
        }

        public static void Dispose()
        {
            LensFlareCommonSRP.Dispose();
            if (flareMaterial != null)
            {
                CoreUtils.Destroy(flareMaterial);
                flareMaterial = null;
            }
        }
    }
}
