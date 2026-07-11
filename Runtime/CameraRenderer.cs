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

        const int ForwardPlusEnableThreshold = 16;
        const int ForwardPlusDisableThreshold = 8;
        static bool forwardPlusActive;

        static CameraSettings defaultCameraSettings = new CameraSettings();

        PostFXStack postFXStack = new PostFXStack();

        Material material;
#if !UNITY_WEBGL || UNITY_EDITOR
        Shader deferredLightingShader;
#endif

        public CameraRenderer(Shader shader, Shader deferredLightingShader,
            Shader forwardPlusDebuggerShader, Shader depthDebuggerShader,
            Shader taaShader, ComputeShader forwardPlusCullCompute)
        {
            material = CoreUtils.CreateEngineMaterial(shader);
            ForwardPlusDebugger.Initialize(forwardPlusDebuggerShader);
#if !UNITY_WEBGL || UNITY_EDITOR
            this.deferredLightingShader = deferredLightingShader;
#endif
            DepthDebugger.Initialize(depthDebuggerShader);
            TAAResolvePass.SetShader(taaShader);
            LightingPass.CullComputeShader = forwardPlusCullCompute;
        }

        void SetupPostFXStack(
            Camera camera, CameraSettings cameraSettings,
            in CameraBufferSettings bufferSettings, PostFXSettings postFXSettings,
            Vector2Int bufferSize, bool useGBufferNormals,
            TextureHandle gBufferNormalMS, bool useHDR, MSAASamples msaaSamples,
            in ShadowTextures shadowTextures)
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
            postFXStack.ShadowDirectionalAtlas = shadowTextures.directionalAtlas;
            postFXStack.ShadowOtherAtlas = shadowTextures.otherAtlas;
            postFXStack.VolumeStack = VolumeManager.instance.stack;
            postFXStack.VolumeLayerMask = cameraSettings.volumeLayerMask;
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
            TaoTieRenderPipelineSettings settings, Camera camera, bool hasActivePostFX,
            Vector2Int bufferSize, bool useHDR)
        {
            LensFlarePass.Record(renderGraph, camera, textures, useDepthTexture, bufferSize, useHDR);

            if (hasActivePostFX)
            {
                if (!PostFXPass.Record(
                    renderGraph, postFXStack, colorLUTResolution, textures))
                {
                    FinalPass.Record(renderGraph, copier, textures);
                }
            }
            else
            {
                FinalPass.Record(renderGraph, copier, textures);
            }
            DepthDebuggerPass.Record(renderGraph, textures, useDepthTexture);
            ForwardPlusDebuggerPass.Record(renderGraph, settings, camera);
            GizmosPass.Record(renderGraph, copier, textures);
        }

        /// <summary>
        /// Main render entry point. Render pipeline order:
        /// Forward: Lighting → Setup → [DepthPrePass] → [ForwardPlusCull] → Geometry(opaque) → Skybox
        ///          → [Resolve(MSAA)] → CopyAttachments → [SSAO] → Geometry(transparent)
        ///          → [Resolve] → [TAA] → LensFlare → PostFX → Final → Debug → Gizmos
        /// Deferred: Lighting → Setup → [DepthPrePass] → [ForwardPlusCull] → GBuffer → DeferredLighting → Skybox
        ///          → CopyAttachments → [SSAO] → Geometry(transparent)
        ///          → [TAA] → LensFlare → PostFX → Final → Debug → Gizmos
        /// DepthPrePass runs before both paths when depthPrimingMode=Forced, or in Forward when MSAA+CopyDepth (Auto).
        /// In Deferred, Auto never triggers DepthPrePass (MSAA always off).
        /// </summary>
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
                    case CameraBufferSettings.HighQualityAAMode.MSAA2x:
                    case CameraBufferSettings.HighQualityAAMode.MSAA4x:
                    case CameraBufferSettings.HighQualityAAMode.MSAA8x:
                        msaaSamples = (MSAASamples)bufferSettings.highQualityAA;
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

            if (useTAA)
            {
                useDepthTexture = true;
            }

            // Determine Forward+ early — needed for depth prepass decision
            int otherVisibleLightCount = 0;
            var visibleLights = cullingResults.visibleLights;
            for (int vi = 0; vi < visibleLights.Length; vi++)
            {
                var lt = visibleLights[vi].lightType;
                if (lt == LightType.Point || lt == LightType.Spot)
                    otherVisibleLightCount++;
            }
            bool useForwardPlus = shadowSettings.forwardPlus switch
            {
                ShadowSettings.ForwardPlusMode.Off => false,
                ShadowSettings.ForwardPlusMode.Auto => forwardPlusActive
                    ? otherVisibleLightCount >= ForwardPlusDisableThreshold
                    : otherVisibleLightCount > ForwardPlusEnableThreshold,
                ShadowSettings.ForwardPlusMode.Force => true,
                _ => false
            } && SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2;
            forwardPlusActive = useForwardPlus;

            bool useDepthPrePass = bufferSettings.depthPrimingMode switch
            {
                CameraBufferSettings.DepthPrimingMode.Auto => !useDeferred && useMSAA && useDepthTexture,
                CameraBufferSettings.DepthPrimingMode.Forced => true,
                _ => false
            };
            // Forward+ 2.5D depth culling only when DepthPrePass is actually available
            bool useDepth25D = useForwardPlus && useDepthPrePass;
            // MRT + MSAA is not reliably supported; disable MSAA for deferred.
            if (useDeferred)
            {
                msaaSamples = MSAASamples.None;
                useMSAA = false;
            }
            
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

            VolumeManager.instance.Update(camera.transform, cameraSettings.volumeLayerMask);

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

                if (useForwardPlus)
                    Shader.EnableKeyword("_TAOTIE_FORWARD_PLUS");
                else
                    Shader.DisableKeyword("_TAOTIE_FORWARD_PLUS");
                ShadowTextures shadowTextures = LightingPass.Record(
                    renderGraph, cullingResults, bufferSize,shadowSettings,
                    cameraSettings.maskLights ? cameraSettings.renderingLayerMask :
                        -1, useForwardPlus, useDepth25D, camera);

                CameraRendererTextures textures = SetupPass.Record(
                    renderGraph, useColorTexture, useDepthTexture,
                    useHDR, bufferSize, camera, msaaSamples,
                    taaJitter, useTAA);
                var copier = new CameraRendererCopier(material, camera, cameraSettings.finalBlendMode);
                if (useDepthPrePass)
                {
                    DepthPrePass.Record(renderGraph, camera, cullingResults,
                        cameraSettings.renderingLayerMask, textures);
                }
                if (useDeferred)
                {
#if !UNITY_WEBGL || UNITY_EDITOR
                    // --- Deferred path ---
                    // Forward+ tile culling before GBuffer (depth from prepass)
                    if (useForwardPlus)
                        ForwardPlusCullPass.Record(renderGraph, textures, useDepth25D);

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
                        true, gBuffer.normalMetallicSmoothness, useHDR, msaaSamples,
                        shadowTextures);
                    RecordPostFXAndDebug(renderGraph, copier, textures, useDepthTexture,
                        postFXStack, (int) settings.colorLUTResolution,
                        settings, camera, hasActivePostFX, bufferSize, useHDR);
#endif
                }
                else
                {
                    // Forward+ tile culling (after depth prepass, before geometry)
                    if (useForwardPlus)
                        ForwardPlusCullPass.Record(renderGraph, textures, useDepth25D);

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
                            false, default, useHDR, MSAASamples.None,
                            shadowTextures);
                        RecordPostFXAndDebug(renderGraph, copier, textures, useDepthTexture,
                            postFXStack, (int) settings.colorLUTResolution,
                            settings, camera, hasActivePostFX, bufferSize, useHDR);
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
#if !UNITY_WEBGL || UNITY_EDITOR
            DeferredLightingPass.Dispose();
#endif
            DepthDebugger.Cleanup();
            TAAResolvePass.Dispose();
            TAACameraData.CleanupAll();
            SMAATextures.Dispose();
            SSAOPass.Dispose();
            LensFlarePass.Dispose();
            CameraRendererCopier.Cleanup();
        }
    }
}
