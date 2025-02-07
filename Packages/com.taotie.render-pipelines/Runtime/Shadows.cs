using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;

namespace TaoTie
{
    public partial class Shadows
    {
        const int maxShadowedDirLightCount = 4, maxShadowedOtherLightCount = 16;
        const int maxCascades = 4;

        struct ShadowedDirectionalLight
        {
            public int visibleLightIndex;
            public float slopeScaleBias;
            public float nearPlaneOffset;
        }

        struct ShadowedOtherLight
        {
            public int visibleLightIndex;
            public float slopeScaleBias;
            public float normalBias;
            public bool isPoint;
        }

        ShadowedDirectionalLight[] shadowedDirLights =
            new ShadowedDirectionalLight[maxShadowedDirLightCount];

        ShadowedOtherLight[] shadowedOtherLights =
            new ShadowedOtherLight[maxShadowedOtherLightCount];

        static readonly GlobalKeyword[] directionalFilterKeywords =
        {
            GlobalKeyword.Create("_DIRECTIONAL_PCF3"),
            GlobalKeyword.Create("_DIRECTIONAL_PCF5"),
            GlobalKeyword.Create("_DIRECTIONAL_PCF7"),
        };

        static readonly GlobalKeyword[] otherFilterKeywords =
        {
            GlobalKeyword.Create("_OTHER_PCF3"),
            GlobalKeyword.Create("_OTHER_PCF5"),
            GlobalKeyword.Create("_OTHER_PCF7"),
        };

        static readonly GlobalKeyword[] cascadeBlendKeywords =
        {
            GlobalKeyword.Create("_CASCADE_BLEND_SOFT"),
            GlobalKeyword.Create("_CASCADE_BLEND_DITHER"),
        };

        static readonly GlobalKeyword[] shadowMaskKeywords =
        {
            GlobalKeyword.Create("_SHADOW_MASK_ALWAYS"),
            GlobalKeyword.Create("_SHADOW_MASK_DISTANCE"),
        };

        static int
            directionalShadowAtlasId = Shader.PropertyToID("_DirectionalShadowAtlas"),
            directionalShadowCascadesId = Shader.PropertyToID("_DirectionalShadowCascades"),
            directionalShadowMatricesId = Shader.PropertyToID("_DirectionalShadowMatrices"),
            otherShadowAtlasId = Shader.PropertyToID("_OtherShadowAtlas"),
            otherShadowDataId = Shader.PropertyToID("_OtherShadowData"),
            cascadeCountId = Shader.PropertyToID("_CascadeCount"),
            shadowAtlasSizeId = Shader.PropertyToID("_ShadowAtlasSize"),
            shadowDistanceFadeId = Shader.PropertyToID("_ShadowDistanceFade"),
            shadowPancakingId = Shader.PropertyToID("_ShadowPancaking");

        static readonly DirectionalShadowCascade[] directionalShadowCascades =
            new DirectionalShadowCascade[maxCascades];

        private static Matrix4x4[]
            dirShadowMatrices = new Matrix4x4[maxShadowedDirLightCount * maxCascades];

        static readonly OtherShadowData[] otherShadowData =
            new OtherShadowData[maxShadowedOtherLightCount];

        ScriptableRenderContext context;
        CullingResults cullingResults;
        bool useShadowMask;
        ShadowSettings settings;

        int shadowedDirLightCount, shadowedOtherLightCount;
        Vector4 atlasSizes;
        CommandBuffer buffer;
        TextureHandle directionalAtlas, otherAtlas;

        ComputeBufferHandle
            directionalShadowCascadesBuffer,
            directionalShadowMatricesBuffer,
            otherShadowDataBuffer;

        public void Setup(CullingResults cullingResults,
            ShadowSettings settings
        )
        {
            this.cullingResults = cullingResults;
            this.settings = settings;
            shadowedDirLightCount = shadowedOtherLightCount = 0;
            useShadowMask = false;
        }

        public ShadowResources GetResources(
            RenderGraph renderGraph,
            RenderGraphBuilder builder)
        {
            int atlasSize = (int) settings.directional.atlasSize;
            var desc = new TextureDesc(atlasSize, atlasSize)
            {
                depthBufferBits = DepthBits.Depth32,
                isShadowMap = true,
                name = "Directional Shadow Atlas"
            };
            directionalAtlas = shadowedDirLightCount > 0
                ? builder.WriteTexture(renderGraph.CreateTexture(desc))
                : renderGraph.defaultResources.defaultShadowTexture;

            directionalShadowCascadesBuffer = builder.WriteComputeBuffer(
                renderGraph.CreateComputeBuffer(new ComputeBufferDesc
                {
                    name = "Shadow Cascades",
                    stride = DirectionalShadowCascade.stride,
                    count = maxCascades
                }));

            directionalShadowMatricesBuffer = builder.WriteComputeBuffer(
                renderGraph.CreateComputeBuffer(new ComputeBufferDesc
                {
                    name = "Directional Shadow Matrices",
                    stride = 4 * 16,
                    count = maxShadowedDirLightCount * maxCascades
                }));

            atlasSize = (int) settings.other.atlasSize;
            desc.width = desc.height = atlasSize;
            desc.name = "Other Shadow Atlas";
            otherAtlas = shadowedOtherLightCount > 0
                ? builder.WriteTexture(renderGraph.CreateTexture(desc))
                : renderGraph.defaultResources.defaultShadowTexture;

            otherShadowDataBuffer = builder.WriteComputeBuffer(
                renderGraph.CreateComputeBuffer(new ComputeBufferDesc
                {
                    name = "Other Shadow Data",
                    stride = OtherShadowData.stride,
                    count = maxShadowedOtherLightCount
                }));

            return new ShadowResources(
                directionalAtlas,
                otherAtlas,
                directionalShadowCascadesBuffer,
                directionalShadowMatricesBuffer,
                otherShadowDataBuffer);
        }

        void ExecuteBuffer()
        {
            context.ExecuteCommandBuffer(buffer);
            buffer.Clear();
        }

        public Vector4 ReserveDirectionalShadows(
            Light light, int visibleLightIndex
        )
        {
            if (
                shadowedDirLightCount < maxShadowedDirLightCount &&
                light.shadows != LightShadows.None && light.shadowStrength > 0f
            )
            {
                float maskChannel = -1;
                LightBakingOutput lightBaking = light.bakingOutput;
                if (
                    lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                    lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask
                )
                {
                    useShadowMask = true;
                    maskChannel = lightBaking.occlusionMaskChannel;
                }

                if (!cullingResults.GetShadowCasterBounds(
                        visibleLightIndex, out Bounds b
                    ))
                {
                    return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
                }

                shadowedDirLights[shadowedDirLightCount] =
                    new ShadowedDirectionalLight
                    {
                        visibleLightIndex = visibleLightIndex,
                        slopeScaleBias = light.shadowBias,
                        nearPlaneOffset = light.shadowNearPlane
                    };
                return new Vector4(
                    light.shadowStrength,
                    settings.directional.cascadeCount * shadowedDirLightCount++,
                    light.shadowNormalBias, maskChannel
                );
            }

            return new Vector4(0f, 0f, 0f, -1f);
        }

        public void Render(RenderGraphContext context)
        {
            buffer = context.cmd;
            this.context = context.renderContext;
            if (shadowedDirLightCount > 0)
            {
                RenderDirectionalShadows();
            }

            if (shadowedOtherLightCount > 0)
            {
                RenderOtherShadows();
            }

            buffer.SetGlobalBuffer(
                directionalShadowCascadesId, directionalShadowCascadesBuffer);
            buffer.SetGlobalBuffer(
                directionalShadowMatricesId, directionalShadowMatricesBuffer);
            buffer.SetGlobalBuffer(otherShadowDataId, otherShadowDataBuffer);
            buffer.SetGlobalTexture(directionalShadowAtlasId, directionalAtlas);
            buffer.SetGlobalTexture(otherShadowAtlasId, otherAtlas);

            SetKeywords(shadowMaskKeywords,
                useShadowMask ? QualitySettings.shadowmaskMode == ShadowmaskMode.Shadowmask ? 0 : 1 : -1);
            buffer.SetGlobalInt(
                cascadeCountId,
                shadowedDirLightCount > 0 ? settings.directional.cascadeCount : 0
            );
            float f = 1f - settings.directional.cascadeFade;
            buffer.SetGlobalVector(
                shadowDistanceFadeId, new Vector4(
                    1f / settings.maxDistance, 1f / settings.distanceFade,
                    1f / (1f - f * f)
                )
            );
            buffer.SetGlobalVector(shadowAtlasSizeId, atlasSizes);
            ExecuteBuffer();
        }

        void RenderDirectionalShadows()
        {
            int atlasSize = (int) settings.directional.atlasSize;
            atlasSizes.x = atlasSize;
            atlasSizes.y = 1f / atlasSize;

            buffer.SetRenderTarget(
                directionalAtlas,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
            buffer.ClearRenderTarget(true, false, Color.clear);
            buffer.SetGlobalFloat(shadowPancakingId, 1f);
            buffer.BeginSample("Directional Shadows");
            ExecuteBuffer();

            int tiles = shadowedDirLightCount * settings.directional.cascadeCount;
            int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
            int tileSize = atlasSize / split;
            for (int i = 0; i < shadowedDirLightCount; i++)
            {
                RenderDirectionalShadows(i, split, tileSize);
            }

            buffer.SetBufferData(
                directionalShadowCascadesBuffer, directionalShadowCascades,
                0, 0, settings.directional.cascadeCount);
            buffer.SetBufferData(
                directionalShadowMatricesBuffer, dirShadowMatrices,
                0, 0, shadowedDirLightCount * settings.directional.cascadeCount);

            SetKeywords(
                directionalFilterKeywords, (int) settings.directional.filter - 1
            );
            SetKeywords(
                cascadeBlendKeywords, (int) settings.directional.cascadeBlend - 1
            );
            buffer.EndSample("Directional Shadows");
            ExecuteBuffer();
        }

        void RenderDirectionalShadows(int index, int split, int tileSize)
        {
            ShadowedDirectionalLight light = shadowedDirLights[index];
            var shadowSettings =
                new ShadowDrawingSettings(cullingResults, light.visibleLightIndex,
                    BatchCullingProjectionType.Orthographic
                )
                {
                    useRenderingLayerMaskTest = true
                };
            int cascadeCount = settings.directional.cascadeCount;
            int tileOffset = index * cascadeCount;
            Vector3 ratios = settings.directional.CascadeRatios;

            float cullingFactor =
                Mathf.Max(0f, 0.8f - settings.directional.cascadeFade);
            float tileScale = 1f / split;
            for (int i = 0; i < cascadeCount; i++)
            {
                cullingResults.ComputeDirectionalShadowMatricesAndCullingPrimitives(
                    light.visibleLightIndex, i, cascadeCount, ratios, tileSize, light.nearPlaneOffset,
                    out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
                    out ShadowSplitData splitData
                );
                splitData.shadowCascadeBlendCullingFactor = cullingFactor;
                shadowSettings.splitData = splitData;
                if (index == 0)
                {
                    directionalShadowCascades[i] = new DirectionalShadowCascade(
                        splitData.cullingSphere,
                        tileSize, settings.directional.filter);
                }

                int tileIndex = tileOffset + i;
                dirShadowMatrices[tileIndex] = ConvertToAtlasMatrix(
                    projectionMatrix * viewMatrix,
                    SetTileViewport(tileIndex, split, tileSize), tileScale
                );
                buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);

                buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
                ExecuteBuffer();
                context.DrawShadows(ref shadowSettings);
                buffer.SetGlobalDepthBias(0f, 0f);
            }
        }

        void RenderOtherShadows()
        {
            int atlasSize = (int) settings.other.atlasSize;
            atlasSizes.z = atlasSize;
            atlasSizes.w = 1f / atlasSize;

            buffer.SetRenderTarget(
                otherAtlas,
                RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store
            );
            buffer.ClearRenderTarget(true, false, Color.clear);
            buffer.SetGlobalFloat(shadowPancakingId, 0f);
            buffer.BeginSample("Other Shadows");
            ExecuteBuffer();

            int tiles = shadowedOtherLightCount;
            int split = tiles <= 1 ? 1 : tiles <= 4 ? 2 : 4;
            int tileSize = atlasSize / split;

            for (int i = 0; i < shadowedOtherLightCount;)
            {
                //i++) {
                if (shadowedOtherLights[i].isPoint)
                {
                    RenderPointShadows(i, split, tileSize);
                    i += 6;
                }
                else
                {
                    RenderSpotShadows(i, split, tileSize);
                    i += 1;
                }
            }

            buffer.SetBufferData(
                otherShadowDataBuffer, otherShadowData,
                0, 0, shadowedOtherLightCount);
            buffer.SetGlobalBuffer(otherShadowDataId, otherShadowDataBuffer);
            SetKeywords(
                otherFilterKeywords, (int) settings.other.filter - 1
            );
            buffer.EndSample("Other Shadows");
            ExecuteBuffer();
        }

        void RenderSpotShadows(int index, int split, int tileSize)
        {
            ShadowedOtherLight light = shadowedOtherLights[index];
            var shadowSettings = new ShadowDrawingSettings(
                cullingResults, light.visibleLightIndex,
                BatchCullingProjectionType.Orthographic
            )
            {
                useRenderingLayerMaskTest = true
            };
            cullingResults.ComputeSpotShadowMatricesAndCullingPrimitives(
                light.visibleLightIndex, out Matrix4x4 viewMatrix,
                out Matrix4x4 projectionMatrix, out ShadowSplitData splitData
            );
            shadowSettings.splitData = splitData;
            float texelSize = 2f / (tileSize * projectionMatrix.m00);
            float filterSize = texelSize * ((float) settings.other.filter + 1f);
            float bias = light.normalBias * filterSize * 1.4142136f;
            Vector2 offset = SetTileViewport(index, split, tileSize);
            float tileScale = 1f / split;
            otherShadowData[index] = new OtherShadowData(
                offset, tileScale, bias, atlasSizes.w * 0.5f,
                ConvertToAtlasMatrix(
                    projectionMatrix * viewMatrix, offset, tileScale));
            buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
            buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
            ExecuteBuffer();
            context.DrawShadows(ref shadowSettings);
            buffer.SetGlobalDepthBias(0f, 0f);
        }

        void RenderPointShadows(int index, int split, int tileSize)
        {
            ShadowedOtherLight light = shadowedOtherLights[index];
            var shadowSettings = new ShadowDrawingSettings(
                cullingResults, light.visibleLightIndex,
                BatchCullingProjectionType.Orthographic
            )
            {
                useRenderingLayerMaskTest = true
            };
            float texelSize = 2f / tileSize;
            float filterSize = texelSize * ((float) settings.other.filter + 1f);
            float bias = light.normalBias * filterSize * 1.4142136f;
            float tileScale = 1f / split;
            float fovBias =
                Mathf.Atan(1f + bias + filterSize) * Mathf.Rad2Deg * 2f - 90f;
            for (int i = 0; i < 6; i++)
            {
                cullingResults.ComputePointShadowMatricesAndCullingPrimitives(
                    light.visibleLightIndex, (CubemapFace) i, fovBias,
                    out Matrix4x4 viewMatrix, out Matrix4x4 projectionMatrix,
                    out ShadowSplitData splitData
                );
                viewMatrix.m11 = -viewMatrix.m11;
                viewMatrix.m12 = -viewMatrix.m12;
                viewMatrix.m13 = -viewMatrix.m13;
                shadowSettings.splitData = splitData;
                int tileIndex = index + i;

                Vector2 offset = SetTileViewport(tileIndex, split, tileSize);

                otherShadowData[tileIndex] = new OtherShadowData(
                    offset, tileScale, bias, atlasSizes.w * 0.5f,
                    ConvertToAtlasMatrix(
                        projectionMatrix * viewMatrix, offset, tileScale));

                buffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
                buffer.SetGlobalDepthBias(0f, light.slopeScaleBias);
                ExecuteBuffer();
                context.DrawShadows(ref shadowSettings);
                buffer.SetGlobalDepthBias(0f, 0f);
            }
        }

        public Vector4 ReserveOtherShadows(Light light, int visibleLightIndex)
        {
            if (light.shadows == LightShadows.None || light.shadowStrength <= 0f)
            {
                return new Vector4(0f, 0f, 0f, -1f);
            }

            float maskChannel = -1f;
            LightBakingOutput lightBaking = light.bakingOutput;
            if (
                lightBaking.lightmapBakeType == LightmapBakeType.Mixed &&
                lightBaking.mixedLightingMode == MixedLightingMode.Shadowmask
            )
            {
                useShadowMask = true;
                maskChannel = lightBaking.occlusionMaskChannel;
            }

            bool isPoint = light.type == LightType.Point;
            int newLightCount = shadowedOtherLightCount + (isPoint ? 6 : 1);
            if (newLightCount > maxShadowedOtherLightCount ||
                !cullingResults.GetShadowCasterBounds(visibleLightIndex, out Bounds b)
               )
            {
                return new Vector4(-light.shadowStrength, 0f, 0f, maskChannel);
            }

            shadowedOtherLights[shadowedOtherLightCount] = new ShadowedOtherLight
            {
                visibleLightIndex = visibleLightIndex,
                slopeScaleBias = light.shadowBias,
                normalBias = light.shadowNormalBias,
                isPoint = isPoint
            };

            Vector4 data = new Vector4(
                light.shadowStrength, shadowedOtherLightCount,
                isPoint ? 1f : 0f, maskChannel
            );
            shadowedOtherLightCount = newLightCount;
            return data;
        }

        Vector2 SetTileViewport(int index, int split, float tileSize)
        {
            Vector2 offset = new Vector2(index % split, index / split);
            buffer.SetViewport(new Rect(
                offset.x * tileSize, offset.y * tileSize, tileSize, tileSize
            ));
            return offset;
        }

        Matrix4x4 ConvertToAtlasMatrix(Matrix4x4 m, Vector2 offset, float scale)
        {
            if (SystemInfo.usesReversedZBuffer)
            {
                m.m20 = -m.m20;
                m.m21 = -m.m21;
                m.m22 = -m.m22;
                m.m23 = -m.m23;
            }

            m.m00 = (0.5f * (m.m00 + m.m30) + offset.x * m.m30) * scale;
            m.m01 = (0.5f * (m.m01 + m.m31) + offset.x * m.m31) * scale;
            m.m02 = (0.5f * (m.m02 + m.m32) + offset.x * m.m32) * scale;
            m.m03 = (0.5f * (m.m03 + m.m33) + offset.x * m.m33) * scale;
            m.m10 = (0.5f * (m.m10 + m.m30) + offset.y * m.m30) * scale;
            m.m11 = (0.5f * (m.m11 + m.m31) + offset.y * m.m31) * scale;
            m.m12 = (0.5f * (m.m12 + m.m32) + offset.y * m.m32) * scale;
            m.m13 = (0.5f * (m.m13 + m.m33) + offset.y * m.m33) * scale;
            m.m20 = 0.5f * (m.m20 + m.m30);
            m.m21 = 0.5f * (m.m21 + m.m31);
            m.m22 = 0.5f * (m.m22 + m.m32);
            m.m23 = 0.5f * (m.m23 + m.m33);
            return m;
        }

        void SetKeywords(GlobalKeyword[] keywords, int enabledIndex)
        {
            for (int i = 0; i < keywords.Length; i++)
            {
                buffer.SetKeyword(keywords[i], i == enabledIndex);
            }
        }

    }
}