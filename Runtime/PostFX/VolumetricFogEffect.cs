using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class VolumetricFogEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Volumetric Fog");

        static readonly int
            vfogSourceID = Shader.PropertyToID("_VFogSource"),
            vfogSampleCountID = Shader.PropertyToID("_VFogSampleCount"),
            vfogStepParamsID = Shader.PropertyToID("_VFogStepParams"),
            vfogJitterID = Shader.PropertyToID("_VFogJitter"),
            vfogScatteringID = Shader.PropertyToID("_VFogScattering"),
            vfogExtinctionID = Shader.PropertyToID("_VFogExtinction"),
            vfogMieGID = Shader.PropertyToID("_VFogMieG"),
            vfogDensityID = Shader.PropertyToID("_VFogDensity"),
            vfogColorID = Shader.PropertyToID("_VFogColor"),
            vfogMaxDistanceID = Shader.PropertyToID("_VFogMaxDistance");

        static Material vfogMaterial;

        [HideInInspector] public Shader vfogShader;

        [System.Serializable]
        public struct FogSettings
        {
            [Range(1, 64)] public int sampleCount;
            [Range(0.01f, 5f)] public float minStep;
            [Range(0.1f, 20f)] public float maxStep;
            [Range(1f, 3f)] public float stepIncrement;
            [Range(0f, 2f)] public float jitter;
            [Range(0f, 5f)] public float scattering;
            [Range(0f, 5f)] public float extinction;
            [Range(0f, 0.99f)] public float mieG;
            [Range(0f, 1f)] public float density;
            [Range(10f, 2000f)] public float maxDistance;
            public Color color;
        }

        [SerializeField] public FogSettings settings = new FogSettings
        {
            sampleCount = 32,
            minStep = 0.2f,
            maxStep = 3.0f,
            stepIncrement = 1.3f,
            jitter = 0.5f,
            scattering = 0.5f,
            extinction = 0.5f,
            mieG = 0.3f,
            density = 0.5f,
            maxDistance = 200f,
            color = new Color(0.5f, 0.6f, 0.7f, 1f),
        };

        public FogSettings Settings => settings;

        public override string DisplayName => "Volumetric Fog";

        public override string ShaderName => "Hidden/TaoTie RP/Volumetric Fog";

        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (vfogShader == null)
                vfogShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph,
            PostFXStack stack,
            TextureHandle source,
            in CameraRendererTextures textures)
        {
            if (!IsEnabled)
                return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (vfogMaterial == null)
                return source;

            TextureHandle depthTex = textures.depthCopy.IsValid()
                ? textures.depthCopy
                : textures.depthAttachment;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out VFogRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.depthTexture = builder.ReadTexture(depthTex);
            pass.camera = camera;
            pass.bufferSize = stack.BufferSize;
            pass.sampleCount = settings.sampleCount;
            pass.stepParams = new Vector4(settings.minStep, settings.maxStep, settings.stepIncrement, settings.maxDistance);
            pass.jitter = settings.jitter;
            pass.scattering = settings.scattering;
            pass.extinction = settings.extinction;
            pass.mieG = settings.mieG;
            pass.density = settings.density;
            pass.fogColor = settings.color;
            pass.maxDistance = settings.maxDistance;

            var resultDesc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Volumetric Fog Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(resultDesc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<VFogRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = vfogShader;
            if (shader == null)
            {
                if (cachedShader == null)
                    cachedShader = Shader.Find(ShaderName);
                shader = cachedShader;
            }
            else
            {
                cachedShader = shader;
            }
            if (shader == null)
            {
                vfogMaterial = null;
                return;
            }
            if (vfogMaterial == null || vfogMaterial.shader != shader)
            {
                if (vfogMaterial != null)
                    CoreUtils.Destroy(vfogMaterial);
                vfogMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            if (!disposedRegistered)
            {
                disposedRegistered = true;
                RegisterDispose(Dispose);
            }
        }

        protected override void DisposeInternal() => Dispose();

        static void Dispose()
        {
            if (vfogMaterial != null)
            {
                CoreUtils.Destroy(vfogMaterial);
                vfogMaterial = null;
            }
        }

        class VFogRenderPass
        {
            public TextureHandle source;
            public TextureHandle depthTexture;
            public Camera camera;
            public Vector2Int bufferSize;
            public int sampleCount;
            public Vector4 stepParams;
            public float jitter;
            public float scattering;
            public float extinction;
            public float mieG;
            public float density;
            public Color fogColor;
            public float maxDistance;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(vfogSourceID, source);
                cmd.SetGlobalTexture("_CameraDepthTexture", depthTexture);
                cmd.SetGlobalInt(vfogSampleCountID, sampleCount);
                cmd.SetGlobalVector(vfogStepParamsID, stepParams);
                cmd.SetGlobalFloat(vfogJitterID, jitter);
                cmd.SetGlobalFloat(vfogScatteringID, scattering);
                cmd.SetGlobalFloat(vfogExtinctionID, extinction);
                cmd.SetGlobalFloat(vfogMieGID, mieG);
                cmd.SetGlobalFloat(vfogDensityID, density);
                cmd.SetGlobalColor(vfogColorID, fogColor);
                cmd.SetGlobalFloat(vfogMaxDistanceID, maxDistance);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, vfogMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
