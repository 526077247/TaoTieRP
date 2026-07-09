using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class MotionBlurEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Motion Blur");
        static readonly int
            mbSourceID = Shader.PropertyToID("_MBSource"),
            mbIntensityID = Shader.PropertyToID("_MBIntensity"),
            mbSampleCountID = Shader.PropertyToID("_MBSampleCount"),
            mbInverseVPID = Shader.PropertyToID("_MBInverseVP"),
            mbPreviousVPID = Shader.PropertyToID("_MBPreviousVP");

        static Material mbMaterial;

        [HideInInspector] public Shader mbShader;

        [System.Serializable]
        public struct MBSettings
        {
            public float intensity;
            public int sampleCount;
        }

        [System.NonSerialized] public MBSettings settings;

        public override string DisplayName => "Motion Blur";
        public override string ShaderName => "Hidden/TaoTie RP/Motion Blur";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (mbShader == null)
                mbShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<MotionBlurVolume>();
            if (vol == null) return source;
            settings = new MBSettings
            {
                intensity = vol.intensity.value,
                sampleCount = vol.sampleCount.value
            };

            if (!IsEnabled || settings.intensity <= 0f) return source;

            EnsureMaterial();
            if (mbMaterial == null) return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            // Get or create motion data for this camera
            MotionCameraData motionData = MotionCameraData.Get(camera);

            // Compute current frame VP and inverse
            // Current frame VP and inverse �?use cameraToWorldMatrix (inverse view) instead of full Inverse(VP)
            Matrix4x4 gpuProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false);
            Matrix4x4 currentVP = gpuProj * camera.worldToCameraMatrix;
            // Inverse VP: inverse projection * inverse view = inverse(gpuProj) * cameraToWorld
            Matrix4x4 inverseVP = Matrix4x4.Inverse(gpuProj) * camera.cameraToWorldMatrix;

            // If no history (first frame), just store and return source
            if (!motionData.hasHistory)
            {
                motionData.prevViewProjMatrix = currentVP;
                motionData.hasHistory = true;
                return source;
            }

            TextureHandle depthTex = textures.depthCopy.IsValid()
                ? textures.depthCopy
                : textures.depthAttachment;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out MBRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.depthTexture = builder.ReadTexture(depthTex);
            pass.camera = camera;
            pass.bufferSize = stack.BufferSize;
            pass.intensity = settings.intensity;
            pass.sampleCount = settings.sampleCount;
            pass.inverseVP = inverseVP;
            pass.previousVP = motionData.prevViewProjMatrix;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Motion Blur Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<MBRenderPass>(
                static (pass, context) => pass.Render(context));

            // Update previous VP for next frame
            motionData.prevViewProjMatrix = currentVP;

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = mbShader;
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
                mbMaterial = null;
                return;
            }
            if (mbMaterial == null || mbMaterial.shader != shader)
            {
                if (mbMaterial != null) CoreUtils.Destroy(mbMaterial);
                mbMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (mbMaterial != null)
            {
                CoreUtils.Destroy(mbMaterial);
                mbMaterial = null;
            }
        }

        class MBRenderPass
        {
            public TextureHandle source;
            public TextureHandle depthTexture;
            public Camera camera;
            public Vector2Int bufferSize;
            public float intensity;
            public int sampleCount;
            public Matrix4x4 inverseVP;
            public Matrix4x4 previousVP;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(mbSourceID, source);
                cmd.SetGlobalTexture("_CameraDepthTexture", depthTexture);
                cmd.SetGlobalFloat(mbIntensityID, intensity);
                cmd.SetGlobalInt(mbSampleCountID, sampleCount);
                cmd.SetGlobalMatrix(mbInverseVPID, inverseVP);
                cmd.SetGlobalMatrix(mbPreviousVPID, previousVP);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, mbMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
