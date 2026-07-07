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

        public CameraRenderer(Shader shader, Shader deferredLightingShader,
            Shader forwardPlusDebuggerShader, Shader depthDebuggerShader,
            Shader taaShader, Shader outlineShader)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            ForwardPlusDebugger.Initialize(forwardPlusDebuggerShader);
            this.deferredLightingShader = deferredLightingShader;
            DepthDebugger.Initialize(depthDebuggerShader);
            TAAResolvePass.SetShader(taaShader);
            OutlineEffect.SetShader(outlineShader);
        }

        void SetupPostFXStack(
            Camera camera, CameraSettings cameraSettings,
            in CameraBufferSettings bufferSettings, PostFXSettings postFXSettings,
            Vector2Int bufferSize, bool useGBufferNormals,
            TextureHandle gBufferNormalMS, bool useHDR, MSAASamples msaaSamples)
        {
            postFXStack.BufferSettings = bufferSettings;
            postFXStack.BufferSize = bufferSize;
            postFXStack.Camera = camera;
            postFXStack.FinalBlendMode = cameraSettings.finalBlendMode;
            postFXStack.Settings = postFXSettings;
            postFXStack.UseGBufferNormals = useGBufferNormals;
            postFXStack.GBufferNormalMS = gBufferNormalMS;
            postFXStack.UseHDR = useHDR;
            postFXStack.MSAA = msaaSamples;
            postFXStack.InitializePassMap();
        }

        void RecordTAA(
            RenderGraph renderGraph, in CameraRendererTextures textures,
            TAACameraData taaData, Vector2Int bufferSize,
            in CameraBufferSettings.TAASettings taaSettings,
            Matrix4x4 nonJitteredProj, Camera camera, bool useHDR)
        {
            Matrix4x4 invNonJitteredVP = Matrix4x4.Inverse(
                GL.GetGPUProjectionMatrix(nonJitteredProj, false) * camera.worldToCameraMatrix);
            TextureHandle historyRT = renderGraph.ImportTexture(taaData.historyRT);
            TAAResolvePass.Record(
                renderGraph, textures, historyRT, bufferSize,
                taaData.hasHistory ? (1f - taaSettings.baseBlendFactor) : 1f,
                taaSettings.varianceClampScale,
                taaData.GetJitter(),
                invNonJitteredVP, taaData.prevViewProjMatrix, camera, useHDR);
        }

        void RecordPostFXAndDebug(
            RenderGraph renderGraph, CameraRendererCopier copier,
            in CameraRendererTextures textures, bool useDepthTexture,
            PostFXStack postFXStack, int colorLUTResolution,
            TaoTieRenderPipelineSettings settings, Camera camera, bool hasActivePostFX)
        {
            if (hasActivePostFX)
            {
                PostFXPass.Record(
                    renderGraph, postFXStack, colorLUTResolution, textures);
            }
            else
            {
                FinalPass.Record(renderGraph, copier, textures);
            }
            DepthDebuggerPass.Record(renderGraph, textures, useDepthTexture);
            ForwardPlusDebuggerPass.Record(renderGraph, settings, camera);
            GizmosPass.Record(renderGraph, copier, textures);
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

            if (!cameraSettings.allowPostProcessAA)
                bufferSettings.postProcessAA = CameraBufferSettings.PostProcessAA.Off;

            bool useMSAA = false;
            bool useTAA = false;
            MSAASamples msaaSamples = MSAASamples.None;

            if (cameraSettings.allowHighQualityAA)
            {
                switch (bufferSettings.highQualityAA)
                {
                    case CameraBufferSettings.HighQualityAAMode.MSAA:
                        msaaSamples = bufferSettings.msaaSamples;
                        if (camera.cameraType == CameraType.SceneView ||
                            camera.cameraType == CameraType.Preview ||
                            camera.targetTexture != null)
                        {
                            msaaSamples = MSAASamples.None;
                        }
                        if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
                        {
                            msaaSamples = MSAASamples.None;
                        }
                        useMSAA = msaaSamples != MSAASamples.None;
                        break;
                    case CameraBufferSettings.HighQualityAAMode.TAA:
                        useTAA = !isReflectionCamera &&
                                 camera.cameraType != CameraType.Preview &&
                                 SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;
                        break;
                }
            }

            if (useTAA)
            {
                useDepthTexture = true;
            }

            bool useDepthPrePass = useMSAA && useDepthTexture;

            TAACameraData taaData = null;
            Matrix4x4 nonJitteredProj = camera.projectionMatrix;
            Vector2 taaJitter = Vector2.zero;
            var taaSettings = bufferSettings.taaSettings ?? new CameraBufferSettings.TAASettings();
            if (useTAA)
            {
                taaData = TAACameraData.Get(camera);
                taaData.EnsureHistoryTexture(bufferSize.x, bufferSize.y, useHDR);
                taaData.SetJitterScale(taaSettings.jitterScale);
                taaJitter = taaData.GetJitter();
            }

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
                // WebGL does not support deferred (shader excludes GLES renderers, including GLES3/WebGL2).
#if !UNITY_WEBGL || UNITY_EDITOR
                bool useDeferred = !isReflectionCamera && settings.renderingMode switch
                {
                    TaoTieRenderPipelineSettings.RenderingMode.Deferred =>
                        SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 &&
                        SystemInfo.supportedRenderTargetCount >= 3,
                    TaoTieRenderPipelineSettings.RenderingMode.Forward => false,
                    _ => false
                };
#else
                bool useDeferred = false;
#endif

                // MRT + MSAA is not reliably supported; disable MSAA for deferred.
                if (useDeferred)
                {
                    msaaSamples = MSAASamples.None;
                    useMSAA = false;
                }

                CameraRendererTextures textures = SetupPass.Record(
                    renderGraph, useColorTexture, useDepthTexture,
                    useHDR, bufferSize, camera, msaaSamples,
                    taaJitter, useTAA);
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

                    CopyAttachmentsPass.Record(
                        renderGraph, useColorTexture, useDepthTexture, copier, textures,
                        false);

                    // SSAO (after depth copy, before transparent)
                    bool useSSAO = shadowSettings.ssao.enabled && useDepthTexture &&
                                   !isReflectionCamera;
                    if (useSSAO)
                        SSAOPass.Record(renderGraph, textures, bufferSize, shadowSettings.ssao, camera);
                    Shader.EnableKeyword("_SSAO_ENABLED");
                    if (!useSSAO) Shader.DisableKeyword("_SSAO_ENABLED");

                    // Transparent objects always use forward path (with Forward+ if available).
                    GeometryPass.Record(
                        renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, false, textures, shadowTextures);
                    UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);

                    if (useTAA)
                    {
                        RecordTAA(renderGraph, textures, taaData, bufferSize,
                            taaSettings, nonJitteredProj, camera, useHDR);
                    }

                    SetupPostFXStack(camera, cameraSettings, bufferSettings, postFXSettings, bufferSize,
                        true, gBuffer.normalMetallicSmoothness, useHDR, msaaSamples);
                    RecordPostFXAndDebug(renderGraph, copier, textures, useDepthTexture,
                        postFXStack, (int) settings.colorLUTResolution,
                        settings, camera, hasActivePostFX);
                }
                else
                {
                    // --- Forward path ---
                    if (useDepthPrePass)
                    {
                        DepthPrePass.Record(renderGraph, camera, cullingResults,
                            cameraSettings.renderingLayerMask, textures);
                    }

                    GeometryPass.Record(
                        renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, true, textures, shadowTextures);

                    if (!isReflectionCamera)
                    {
                        SkyboxPass.Record(renderGraph, camera, textures);

                        if (useMSAA)
                        {
                            ResolvePass.Record(renderGraph, textures);
                        }

                        CopyAttachmentsPass.Record(
                            renderGraph, useColorTexture, useDepthTexture && !useDepthPrePass,
                            copier, textures, useMSAA);

                        // SSAO (after depth copy, before transparent)
                        bool useSSAO = shadowSettings.ssao.enabled && useDepthTexture &&
                                       !isReflectionCamera;
                        if (useSSAO)
                            SSAOPass.Record(renderGraph, textures, bufferSize, shadowSettings.ssao, camera);
                        Shader.EnableKeyword("_SSAO_ENABLED");
                        if (!useSSAO) Shader.DisableKeyword("_SSAO_ENABLED");

                        GeometryPass.Record(
                            renderGraph, camera, cullingResults, cameraSettings.renderingLayerMask, false, textures, shadowTextures);
                        UnsupportedShadersPass.Record(renderGraph, camera, cullingResults);
                        if (useMSAA)
                        {
                            ResolvePass.Record(renderGraph, textures);
                        }

                        if (useTAA)
                        {
                            RecordTAA(renderGraph, textures, taaData, bufferSize,
                                taaSettings, nonJitteredProj, camera, useHDR);
                        }

                        SetupPostFXStack(camera, cameraSettings, bufferSettings, postFXSettings, bufferSize,
                            false, default, useHDR, MSAASamples.None);
                        RecordPostFXAndDebug(renderGraph, copier, textures, useDepthTexture,
                            postFXStack, (int) settings.colorLUTResolution,
                            settings, camera, hasActivePostFX);
                    }
                    else
                    {
                        if (useColorTexture || useDepthTexture)
                        {
                            CopyAttachmentsPass.Record(
                                renderGraph, useColorTexture, useDepthTexture,
                                copier, textures, false);
                        }

                        FinalPass.Record(renderGraph, copier, textures);
                    }
                }
            }

            context.ExecuteCommandBuffer(renderGraphParameters.commandBuffer);
            context.Submit();
            CommandBufferPool.Release(renderGraphParameters.commandBuffer);

            // TAA post-frame: update history state
            if (useTAA && taaData != null)
            {
                Matrix4x4 nonJitteredVP = GL.GetGPUProjectionMatrix(nonJitteredProj, false) * camera.worldToCameraMatrix;
                taaData.prevViewProjMatrix = nonJitteredVP;
                taaData.AdvanceFrame();
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(material);
            ForwardPlusDebugger.Cleanup();
            LightingPass.Dispose();
            DeferredLightingPass.Dispose();
            OutlineEffect.Dispose();
            DepthDebugger.Cleanup();
            TAAResolvePass.Dispose();
            TAACameraData.CleanupAll();
            SMAATextures.Dispose();
            SSAOPass.Dispose();
            CameraRendererCopier.Cleanup();
        }
    }
}
