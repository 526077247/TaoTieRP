using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class CameraRenderer
    {
        public const float renderScaleMin = 0.1f, renderScaleMax = 2f;

        static CameraSettings defaultCameraSettings = new CameraSettings();

        PostFXStack postFXStack = new PostFXStack();

        Material material;
        Shader deferredLightingShader;

        public CameraRenderer(Shader shader, Shader cameraDebuggerShader, Shader deferredLightingShader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            CameraDebugger.Initialize(cameraDebuggerShader);
            this.deferredLightingShader = deferredLightingShader;
        }

        public void Render(RenderGraph renderGraph, ScriptableRenderContext context, Camera camera,
            TaoTieRenderPipelineSettings settings)
        {
            CameraBufferSettings bufferSettings = settings.cameraBuffer;
            PostFXSettings postFXSettings = settings.postFXSettings;
            ShadowSettings shadowSettings = settings.shadows;

            ProfilingSampler cameraSampler;
            CameraSettings cameraSettings;
            if (camera.TryGetComponent(out TaoTieRenderPipelineCamera crpCamera))
            {
                cameraSampler = crpCamera.Sampler;
                cameraSettings = crpCamera.Settings;
            }
            else
            {
                cameraSampler = ProfilingSampler.Get(camera.cameraType);
                cameraSettings = defaultCameraSettings;
            }

            bool useColorTexture, useDepthTexture;
            if (camera.cameraType == CameraType.Reflection)
            {
                useColorTexture = bufferSettings.copyColorReflection;
                useDepthTexture = bufferSettings.copyDepthReflection;
            }
            else
            {
                useColorTexture = bufferSettings.copyColor && cameraSettings.copyColor;
                useDepthTexture = bufferSettings.copyDepth && cameraSettings.copyDepth;
            }

            // Reflection probe cameras only need lighting + opaque geometry.
            bool isReflectionCamera = camera.cameraType == CameraType.Reflection;


            if (cameraSettings.overridePostFX)
            {
                postFXSettings = cameraSettings.postFXSettings;
            }

            bool hasActivePostFX =
                postFXSettings != null && PostFXSettings.AreApplicableTo(camera);
            float renderScale = cameraSettings.GetRenderScale(bufferSettings.renderScale);
            bool useScaledRendering = renderScale < 0.99f || renderScale > 1.01f;
#if UNITY_EDITOR
            if (camera.cameraType == CameraType.SceneView)
            {
                ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
                useScaledRendering = false;
            }
#endif
            if (!camera.TryGetCullingParameters(
                    out ScriptableCullingParameters scriptableCullingParameters))
            {
                return;
            }

            scriptableCullingParameters.shadowDistance =
                Mathf.Min(shadowSettings.maxDistance, camera.farClipPlane);
            CullingResults cullingResults = context.Cull(ref scriptableCullingParameters);

            bool useHDR = bufferSettings.allowHDR && camera.allowHDR;
            Vector2Int bufferSize = default;
            if (useScaledRendering)
            {
                renderScale = Mathf.Clamp(renderScale, renderScaleMin, renderScaleMax);
                bufferSize.x = (int) (camera.pixelWidth * renderScale);
                bufferSize.y = (int) (camera.pixelHeight * renderScale);
            }
            else
            {
                bufferSize.x = camera.pixelWidth;
                bufferSize.y = camera.pixelHeight;
            }

            bufferSettings.fxaa &= cameraSettings.allowFXAA;

            MSAASamples msaaSamples = cameraSettings.allowMSAA ? bufferSettings.msaa : MSAASamples.None;
            if (camera.cameraType == CameraType.SceneView ||
                camera.cameraType == CameraType.Preview ||
                camera.targetTexture != null)
            {
                msaaSamples = MSAASamples.None;
            }
            // MSAA resolve requires CopyTexture, which is not available on GLES2/WebGL1.
            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
            {
                msaaSamples = MSAASamples.None;
            }
            bool useMSAA = msaaSamples != MSAASamples.None;

            var renderGraphParameters = new RenderGraphParameters
            {
                commandBuffer = CommandBufferPool.Get(),
                currentFrameIndex = Time.frameCount,
                executionName = cameraSampler.name,
                rendererListCulling = true,
                scriptableRenderContext = context
            };

            using (renderGraph.RecordAndExecute(renderGraphParameters))
            {
                using var _ = new RenderGraphProfilingScope(renderGraph, cameraSampler);

                bool useForwardPlus = settings.shadows.useForwardPlus &&
                                      SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;
                ShadowTextures shadowTextures = LightingPass.Record(
                    renderGraph, cullingResults, bufferSize,shadowSettings,
                    cameraSettings.maskLights ? cameraSettings.renderingLayerMask :
                        -1, useForwardPlus);

                // Decide deferred vs forward: needs MRT (>=3 RT) and not a reflection camera.
                // GLES2/WebGL1 does not support MRT, so always use forward there.
                bool useDeferred = !isReflectionCamera && settings.renderingMode switch
                {
                    TaoTieRenderPipelineSettings.RenderingMode.Deferred =>
                        SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 &&
                        SystemInfo.supportedRenderTargetCount >= 3,
                    TaoTieRenderPipelineSettings.RenderingMode.Forward => false,
                    _ => false
                };

                // MRT + MSAA is not reliably supported; disable MSAA for deferred.
                if (useDeferred)
                {
                    msaaSamples = MSAASamples.None;
                    useMSAA = false;
                }

                CameraRendererTextures textures = SetupPass.Record(
                    renderGraph, useColorTexture, useDepthTexture,
                    useHDR, bufferSize, camera, msaaSamples);
                var copier = new CameraRendererCopier(material, camera, cameraSettings.finalBlendMode);

                if (useDeferred)
                {
                    // --- Deferred path ---
                    GBufferTextures gBuffer = GBufferPass.Record(
                        renderGraph, camera, cullingResults,
                        cameraSettings.renderingLayerMask, useHDR, bufferSize,
                        textures.depthAttachment, shadowTextures);

                    // Deferred lighting writes to color attachment, skips sky pixels (depth clip)
                    DeferredLightingPass.Record(
                        renderGraph, copier, textures, gBuffer, shadowTextures, deferredLightingShader);

                    // Skybox renders after deferred lighting, only where depth is far
                    SkyboxPass.Record(renderGraph, camera, textures);

                    if(bufferSettings.outLine) OutLinePass.Record(
                        renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, textures, shadowTextures);

                    CopyAttachmentsPass.Record(
                        renderGraph, useColorTexture, useDepthTexture, copier, textures);

                    // Transparent objects always use forward path (with Forward+ if available).
                    GeometryPass.Record(
                        renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, false, textures, shadowTextures);
                    UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);
                    if (hasActivePostFX)
                    {
                        postFXStack.BufferSettings = bufferSettings;
                        postFXStack.BufferSize = bufferSize;
                        postFXStack.Camera = camera;
                        postFXStack.FinalBlendMode = cameraSettings.finalBlendMode;
                        postFXStack.Settings = postFXSettings;
                        PostFXPass.Record(
                            renderGraph, postFXStack, (int) settings.colorLUTResolution,
                            textures);
                    }
                    else
                    {
                        FinalPass.Record(renderGraph, copier, textures);
                    }
                    DebugPass.Record(renderGraph, settings, camera);
                    GizmosPass.Record(renderGraph, copier, textures);
                }
                else
                {
                    // --- Forward path ---
                    GeometryPass.Record(
                        renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, true, textures, shadowTextures);

                    if (!isReflectionCamera)
                    {
                        if(bufferSettings.outLine) OutLinePass.Record(
                            renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, textures, shadowTextures);

                        SkyboxPass.Record(renderGraph, camera, textures);

                        if (useMSAA)
                        {
                            ResolvePass.Record(renderGraph, textures);
                        }

                        CopyAttachmentsPass.Record(
                            renderGraph, useColorTexture, useDepthTexture, copier, textures, useMSAA);

                        GeometryPass.Record(
                            renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, false, textures, shadowTextures);
                        UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);
                        if (useMSAA)
                        {
                            ResolvePass.Record(renderGraph, textures);
                        }
                        if (hasActivePostFX)
                        {
                            postFXStack.BufferSettings = bufferSettings;
                            postFXStack.BufferSize = bufferSize;
                            postFXStack.Camera = camera;
                            postFXStack.FinalBlendMode = cameraSettings.finalBlendMode;
                            postFXStack.Settings = postFXSettings;
                            PostFXPass.Record(
                                renderGraph, postFXStack, (int) settings.colorLUTResolution,
                                textures);
                        }
                        else
                        {
                            FinalPass.Record(renderGraph, copier, textures);
                        }
                        DebugPass.Record(renderGraph, settings, camera);
                        GizmosPass.Record(renderGraph, copier, textures);
                    }
                    else
                    {
                        FinalPass.Record(renderGraph, copier, textures);
                    }
                }
            }

            context.ExecuteCommandBuffer(renderGraphParameters.commandBuffer);
            context.Submit();
            CommandBufferPool.Release(renderGraphParameters.commandBuffer);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            CameraDebugger.Cleanup();
            LightingPass.Dispose();
            DeferredLightingPass.Dispose();
        }
    }
}