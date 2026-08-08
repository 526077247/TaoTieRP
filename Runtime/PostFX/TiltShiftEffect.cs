using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public class TiltShiftEffect : PostFXEffect
    {
        static readonly ProfilingSampler sampler = new("Tilt Shift");

        static readonly int
            sourceID = Shader.PropertyToID("_TiltShiftSource"),
            texelSizeID = Shader.PropertyToID("_TiltShiftTexelSize"),
            focusOffsetID = Shader.PropertyToID("_TiltShiftFocusOffset"),
            focusRangeID = Shader.PropertyToID("_TiltShiftFocusRange"),
            smoothnessID = Shader.PropertyToID("_TiltShiftSmoothness"),
            blurStrengthID = Shader.PropertyToID("_TiltShiftBlurStrength"),
            blurDirectionID = Shader.PropertyToID("_TiltShiftBlurDirection");

        static Material tiltShiftMaterial;

        [HideInInspector] public Shader tiltShiftShader;

        [System.Serializable]
        public struct TiltShiftSettings
        {
            public float focusOffset;
            public float focusRange;
            public float smoothness;
            public float blurStrength;
            public int blurDirection;
        }

        [System.NonSerialized] public TiltShiftSettings settings;

        public override string DisplayName => "Tilt Shift";
        public override string ShaderName => "Hidden/TaoTie RP/Tilt Shift";
        public override IReadOnlyList<string> RequiredPassNames => System.Array.Empty<string>();

        public override void EnsureShaderReference()
        {
            if (tiltShiftShader == null)
                tiltShiftShader = Shader.Find(ShaderName);
        }

        public override TextureHandle Execute(
            RenderGraph renderGraph, PostFXStack stack,
            TextureHandle source, in CameraRendererTextures textures)
        {
            var vol = stack.GetActiveVolume<TiltShiftVolume>();
            if (vol == null) return source;
            settings = new TiltShiftSettings
            {
                focusOffset = vol.focusOffset.value,
                focusRange = vol.focusRange.value,
                smoothness = vol.smoothness.value,
                blurStrength = vol.blurStrength.value,
                blurDirection = vol.blurDirection.value
            };
            if (!IsEnabled) return source;

            Camera camera = stack.Camera;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview)
                return source;

            EnsureMaterial();
            if (tiltShiftMaterial == null) return source;

            GraphicsFormat colorFormat = stack.UseHDR &&
                SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                ? GraphicsFormat.R16G16B16A16_SFloat
                : GraphicsFormat.R8G8B8A8_UNorm;

            Vector4 texelSize = new(
                1f / stack.BufferSize.x, 1f / stack.BufferSize.y,
                stack.BufferSize.x, stack.BufferSize.y);

            using RenderGraphBuilder builder = renderGraph.AddRenderPass(
                sampler.name, out TiltShiftRenderPass pass, sampler);

            TextureHandle colorSource = source.IsValid() ? source : textures.colorAttachment;
            pass.source = builder.ReadTexture(colorSource);
            pass.camera = camera;
            pass.bufferSize = stack.BufferSize;
            pass.texelSize = texelSize;
            pass.focusOffset = settings.focusOffset;
            pass.focusRange = settings.focusRange;
            pass.smoothness = settings.smoothness;
            pass.blurStrength = settings.blurStrength;
            pass.blurDirection = settings.blurDirection;

            var desc = new TextureDesc(stack.BufferSize.x, stack.BufferSize.y)
            {
                colorFormat = colorFormat,
                name = "Tilt Shift Result"
            };
            pass.resultTexture = builder.WriteTexture(renderGraph.CreateTexture(desc));

            builder.AllowPassCulling(false);
            builder.SetRenderFunc<TiltShiftRenderPass>(
                static (pass, context) => pass.Render(context));

            return pass.resultTexture;
        }

        static Shader cachedShader;
        static bool disposedRegistered;

        void EnsureMaterial()
        {
            Shader shader = tiltShiftShader;
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
                tiltShiftMaterial = null;
                return;
            }
            if (tiltShiftMaterial == null || tiltShiftMaterial.shader != shader)
            {
                if (tiltShiftMaterial != null) CoreUtils.Destroy(tiltShiftMaterial);
                tiltShiftMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
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
            if (tiltShiftMaterial != null)
            {
                CoreUtils.Destroy(tiltShiftMaterial);
                tiltShiftMaterial = null;
            }
        }

        class TiltShiftRenderPass
        {
            public TextureHandle source;
            public Camera camera;
            public Vector2Int bufferSize;
            public Vector4 texelSize;
            public float focusOffset;
            public float focusRange;
            public float smoothness;
            public float blurStrength;
            public int blurDirection;
            public TextureHandle resultTexture;

            public void Render(RenderGraphContext context)
            {
                CommandBuffer cmd = context.cmd;
                cmd.SetGlobalTexture(sourceID, source);
                cmd.SetGlobalVector(texelSizeID, texelSize);
                cmd.SetGlobalFloat(focusOffsetID, focusOffset);
                cmd.SetGlobalFloat(focusRangeID, focusRange);
                cmd.SetGlobalFloat(smoothnessID, smoothness);
                cmd.SetGlobalFloat(blurStrengthID, blurStrength);
                cmd.SetGlobalFloat(blurDirectionID, (float)blurDirection);

                cmd.SetRenderTarget(resultTexture,
                    RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                cmd.SetViewport(new Rect(0, 0, bufferSize.x, bufferSize.y));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(CameraRendererCopier.FullscreenMesh, Matrix4x4.identity, tiltShiftMaterial, 0, 0);
                cmd.SetViewProjectionMatrices(camera.worldToCameraMatrix, camera.projectionMatrix);

                context.renderContext.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
        }
    }
}
