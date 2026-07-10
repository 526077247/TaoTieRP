using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace TaoTie.RenderPipelines
{
	public class LightingPass
	{
		static readonly ProfilingSampler sampler = new("Lighting");

		private const int maxDirLightCount = 4;

		private static int maxOtherLightCount = SystemInfo.graphicsDeviceType switch
		{
			GraphicsDeviceType.OpenGLES2 => 8,
			_ => 256,
		};
		
		// use Texture2D fallback on those platforms.
		private static readonly bool isOpenGLES = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 &&
		                                          SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3 &&
		                                          SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore;

		static readonly int
			dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
			dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
			dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
			dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
			tilesId = Shader.PropertyToID("_ForwardPlusTilesTex"),
			tilesLightId = Shader.PropertyToID("_ForwardPlusTileLightsTex"),
			tilesBufId = Shader.PropertyToID("_ForwardPlusTilesBuf"),
			tilesLightBufId = Shader.PropertyToID("_ForwardPlusTileLightsBuf"),
			tileSettingsId = Shader.PropertyToID("_ForwardPlusTileSettings"),
			lightTexSizeId = Shader.PropertyToID("_ForwardPlusLightTexSize");

		static readonly Vector4[]
			dirLightColors = new Vector4[maxDirLightCount],
			dirLightDirectionsAndMasks = new Vector4[maxDirLightCount],
			dirLightShadowData = new Vector4[maxDirLightCount];

		static readonly int
			otherLightCountId = Shader.PropertyToID("_OtherLightCount"),
			otherLightColorsId = Shader.PropertyToID("_OtherLightColors"),
			otherLightPositionsId = Shader.PropertyToID("_OtherLightPositions"),
			otherLightDirectionsAndMasksId =
				Shader.PropertyToID("_OtherLightDirectionsAndMasks"),
			otherLightSpotAnglesId = Shader.PropertyToID("_OtherLightSpotAngles"),
			otherLightShadowDataId = Shader.PropertyToID("_OtherLightShadowData");

		static readonly Vector4[]
			otherLightColors = new Vector4[maxOtherLightCount],
			otherLightPositions = new Vector4[maxOtherLightCount],
			otherLightDirectionsAndMasks = new Vector4[maxOtherLightCount],
			otherLightSpotAngles = new Vector4[maxOtherLightCount],
			otherLightShadowData = new Vector4[maxOtherLightCount];

		// Cookie support
		static readonly int
			dirCookieMatrixId = Shader.PropertyToID("_DirLightCookieMatrix"),
			otherCookieMatrixId = Shader.PropertyToID("_OtherLightCookieMatrix"),
			dirCookieEnabledId = Shader.PropertyToID("_DirLightCookieEnabled"),
			otherCookieEnabledId = Shader.PropertyToID("_OtherLightCookieEnabled");

		static readonly Matrix4x4[]
			dirCookieMatrices = new Matrix4x4[maxDirLightCount],
			otherCookieMatrices = new Matrix4x4[maxOtherLightCount];

		static readonly float[]
			dirCookieEnabled = new float[maxDirLightCount],
			otherCookieEnabled = new float[maxOtherLightCount];

		static readonly int[] dirCookieTexIDs =
		{
			Shader.PropertyToID("_DirLightCookie0"),
			Shader.PropertyToID("_DirLightCookie1"),
			Shader.PropertyToID("_DirLightCookie2"),
			Shader.PropertyToID("_DirLightCookie3"),
		};
		static readonly int[] otherCookieTexIDs =
		{
			Shader.PropertyToID("_OtherLightCookie0"),
			Shader.PropertyToID("_OtherLightCookie1"),
			Shader.PropertyToID("_OtherLightCookie2"),
			Shader.PropertyToID("_OtherLightCookie3"),
			Shader.PropertyToID("_OtherLightCookie4"),
			Shader.PropertyToID("_OtherLightCookie5"),
			Shader.PropertyToID("_OtherLightCookie6"),
			Shader.PropertyToID("_OtherLightCookie7"),
		};

		static Texture2D whiteCookieTexture;

		static readonly Texture[] dirCookieTextures = new Texture[maxDirLightCount];
		static readonly Texture[] otherCookieTextures = new Texture[maxOtherLightCount];

		CullingResults cullingResults;

		readonly Shadows shadows = new();

		int dirLightCount, otherLightCount;

		Vector2 screenUVToTileCoordinates;
		Vector2Int tileCount;
		Vector2Int tileDataTexSize;
		int TileCount => tileCount.x * tileCount.y;

		static Vector4[] lightBounds = new Vector4[maxOtherLightCount];
		static NativeArray<float2> tileDataArray;
		static NativeArray<float> tileLightArray;
		private static Texture2D tileDataTexture;
		private static Texture2D tileLightTexture;
		private static ComputeBuffer tileDataBuffer, tileLightBuffer;
		static int tileDataBufferSize, tileLightBufferSize;

		// ComputeShader tile culling (desktop/console platforms)
		public static ComputeShader CullComputeShader { get; set; }
		static int cullKernel = -1;
		static ComputeBuffer lightBoundsBuffer;
		static int lightBoundsBufferSize;

		// Job System tile culling (GLES3/WebGL2 fallback)
		static NativeArray<float4> lightBoundsNative;

		// Dirty-flag: skip GPU upload when tile data hasn't changed.
		// Must be static because RenderGraph creates a new LightingPass instance each frame.
		static readonly Vector4[] lastLightBounds = new Vector4[maxOtherLightCount];
		static int lastOtherLightCount = -1;
		static Vector2Int lastTileCount = new(-1, -1);
		static bool tileDataDirty = true;

		private int maxLightsPerTile;
		private bool useForwardPlus;

		public void Setup(
			CullingResults cullingResults, Vector2Int attachmentSize,
			ShadowSettings shadowSettings, int renderingLayerMask, bool useForwardPlus)
		{
			this.cullingResults = cullingResults;
			shadows.Setup(cullingResults, shadowSettings);
			this.useForwardPlus = useForwardPlus;

			if (useForwardPlus)
			{
				maxLightsPerTile = shadowSettings.other.maxLightsPerTile;

				float tileScreenPixelSize = (float) ShadowSettings.Other.TileSize.Off;
				if (maxLightsPerTile > 0)
				{
					tileScreenPixelSize =
						shadowSettings.other.tileSize <= 0 ? 32f : (float) shadowSettings.other.tileSize;
#if UNITY_WEBGL && !UNITY_EDITOR
					tileScreenPixelSize = Mathf.Max(32f, tileScreenPixelSize);
#else
					if(!isOpenGLES) tileScreenPixelSize = Mathf.Max(32f, tileScreenPixelSize);
#endif
				}
				tileScreenPixelSize *= Mathf.Pow(2,
					Mathf.FloorToInt(Mathf.Sqrt((attachmentSize.x / 1366f) * (attachmentSize.y / 768f)) - 0.1f));
				screenUVToTileCoordinates.x = attachmentSize.x / tileScreenPixelSize;
				screenUVToTileCoordinates.y = attachmentSize.y / tileScreenPixelSize;
				tileCount.x = Mathf.CeilToInt(screenUVToTileCoordinates.x);
				tileCount.y = Mathf.CeilToInt(screenUVToTileCoordinates.y);
			}
			SetupLights(renderingLayerMask,shadowSettings);
		}

		void SetupLights(int renderingLayerMask,ShadowSettings shadowSettings)
		{
			NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
			int i;
			dirLightCount = otherLightCount = 0;

			int max;
			if (useForwardPlus)
			{
				max = maxLightsPerTile * TileCount;
				if (maxOtherLightCount < max)
					max = maxOtherLightCount;
			}
			else
			{
				max = shadowSettings.maxOtherLights;
				if (max > maxOtherLightCount)
				{
					max = maxOtherLightCount;
				}
			}

			for (i = 0; i < visibleLights.Length; i++)
			{
				VisibleLight visibleLight = visibleLights[i];
				Light light = visibleLight.light;
				if ((light.renderingLayerMask & renderingLayerMask) != 0)
				{
					switch (visibleLight.lightType)
					{
						case LightType.Directional:
							if (dirLightCount < maxDirLightCount && dirLightCount< shadowSettings.directional.maxLightCount)
							{
								SetupDirectionalLight(
									dirLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Point:
							if (otherLightCount < max)
							{
								if (useForwardPlus)
									SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupPointLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Spot:
							if (otherLightCount < max)
							{
								if (useForwardPlus)
									SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupSpotLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
					}
				}
			}

			if (useForwardPlus)
			{
				int tileCountTotal = TileCount;
				int maxLightIndices = Mathf.Max(tileCountTotal * maxLightsPerTile, 1);
				EnsureTileTextures(tileCount, maxLightIndices);
				EnsureComputeKernel();
				EnsureLightBoundsNative();

				bool useGpuCompute = SystemInfo.supportsComputeShaders && CullComputeShader != null && cullKernel >= 0;

				#if UNITY_EDITOR
				// In editor, always recompute and upload tile data per camera per frame
				if (useGpuCompute)
				{
					tileDataDirty = true;
					SaveTileDataState();
				}
				else
				{
					BuildTileLightListsJob(tileCountTotal);
					tileDataDirty = true;
				}
				#else
				// Dirty check: compare light bounds, count, and tile grid with last frame
				bool needsRecompute = TileDataNeedsRecompute(tileCountTotal);

				if (needsRecompute)
				{
					if (useGpuCompute)
					{
						tileDataDirty = true;
						SaveTileDataState();
					}
					else
					{
						BuildTileLightListsJob(tileCountTotal);
						tileDataDirty = true;
					}
				}
				else
				{
					tileDataDirty = false;
				}
				#endif
			}
		}

		bool TileDataNeedsRecompute(int tileCountTotal)
		{
			if (otherLightCount != lastOtherLightCount ||
			    tileCount.x != lastTileCount.x ||
			    tileCount.y != lastTileCount.y)
				return true;

			for (int j = 0; j < otherLightCount; j++)
			{
				Vector4 cur = lightBounds[j];
				Vector4 last = lastLightBounds[j];
				if (cur.x != last.x || cur.y != last.y ||
				    cur.z != last.z || cur.w != last.w)
					return true;
			}
			return false;
		}

		void SaveTileDataState()
		{
			lastOtherLightCount = otherLightCount;
			lastTileCount = tileCount;
			for (int j = 0; j < otherLightCount; j++)
				lastLightBounds[j] = lightBounds[j];
		}

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;

			buffer.SetGlobalFloat(dirLightCountId, dirLightCount);
			if (dirLightCount > 0)
			{
				buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
				buffer.SetGlobalVectorArray(
					dirLightDirectionsAndMasksId, dirLightDirectionsAndMasks);
				buffer.SetGlobalVectorArray(
					dirLightShadowDataId, dirLightShadowData);
			}

			buffer.SetGlobalFloat(otherLightCountId, otherLightCount);
			if (otherLightCount > 0)
			{
				buffer.SetGlobalVectorArray(otherLightColorsId, otherLightColors);
				buffer.SetGlobalVectorArray(
					otherLightPositionsId, otherLightPositions);
				buffer.SetGlobalVectorArray(
					otherLightDirectionsAndMasksId, otherLightDirectionsAndMasks);
				buffer.SetGlobalVectorArray(
					otherLightSpotAnglesId, otherLightSpotAngles);
				buffer.SetGlobalVectorArray(
					otherLightShadowDataId, otherLightShadowData);
			}

			// Upload cookie data
			if (whiteCookieTexture == null)
			{
				whiteCookieTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				whiteCookieTexture.SetPixel(0, 0, Color.white);
				whiteCookieTexture.Apply();
				whiteCookieTexture.name = "White Cookie";
			}
			buffer.SetGlobalMatrixArray(dirCookieMatrixId, dirCookieMatrices);
			buffer.SetGlobalFloatArray(dirCookieEnabledId, dirCookieEnabled);
			for (int ci = 0; ci < maxDirLightCount; ci++)
				buffer.SetGlobalTexture(dirCookieTexIDs[ci],
					dirCookieTextures[ci] != null ? dirCookieTextures[ci] : whiteCookieTexture);

			buffer.SetGlobalMatrixArray(otherCookieMatrixId, otherCookieMatrices);
			buffer.SetGlobalFloatArray(otherCookieEnabledId, otherCookieEnabled);
			for (int ci = 0; ci < otherCookieTexIDs.Length; ci++)
			{
				buffer.SetGlobalTexture(otherCookieTexIDs[ci],
					otherCookieTextures[ci] != null ? otherCookieTextures[ci] : whiteCookieTexture);
			}

			shadows.Render(context);

			if (useForwardPlus)
			{
				bool canUseBuffer = isOpenGLES && tileDataBuffer != null && tileLightBuffer != null;
				bool useGpuCompute = canUseBuffer && CullComputeShader != null && cullKernel >= 0;

				if (tileDataDirty)
				{
					if (useGpuCompute)
					{
						lightBoundsBuffer.SetData(lightBounds, 0, 0, otherLightCount);

						CullComputeShader.SetBuffer(cullKernel, "_LightBounds", lightBoundsBuffer);
						CullComputeShader.SetBuffer(cullKernel, "_TileData", tileDataBuffer);
						CullComputeShader.SetBuffer(cullKernel, "_TileLights", tileLightBuffer);
						CullComputeShader.SetInt("_LightCount", otherLightCount);
						CullComputeShader.SetVector("_TileCount", new Vector4(tileCount.x, tileCount.y, tileDataTexSize.x, maxLightsPerTile));
						CullComputeShader.SetVector("_ScreenUVToTileCoords", screenUVToTileCoordinates);

						int groupX = Mathf.CeilToInt(tileCount.x / 8f);
						int groupY = Mathf.CeilToInt(tileCount.y / 8f);
						buffer.DispatchCompute(CullComputeShader, cullKernel, groupX, groupY, 1);
					}
					else if (canUseBuffer)
					{
						tileDataBuffer.SetData(tileDataArray);
						tileLightBuffer.SetData(tileLightArray);
					}
					else
					{
						tileDataTexture.SetPixelData(tileDataArray, 0);
						tileDataTexture.Apply(false);
						tileLightTexture.SetPixelData(tileLightArray, 0);
						tileLightTexture.Apply(false);
					}
					tileDataDirty = false;
				}

				if (canUseBuffer)
				{
					buffer.SetGlobalBuffer(tilesBufId, tileDataBuffer);
					buffer.SetGlobalBuffer(tilesLightBufId, tileLightBuffer);
					// z = tileDataTexSize.x (data stride for linear indexing)
					buffer.SetGlobalVector(lightTexSizeId, new Vector4(
						tileLightBufferSize, 1, tileDataTexSize.x, 0));
				}
				else
				{
					buffer.SetGlobalTexture(tilesId, tileDataTexture);
					buffer.SetGlobalTexture(tilesLightId, tileLightTexture);
					buffer.SetGlobalVector(lightTexSizeId, new Vector4(
						tileLightTexture.width, tileLightTexture.height, 0, 0));
				}
				buffer.SetGlobalVector(tileSettingsId, new Vector4(
					screenUVToTileCoordinates.x, screenUVToTileCoordinates.y,
					tileCount.x,
					maxLightsPerTile));
			}
			else
			{
				buffer.SetGlobalVector(tileSettingsId, new Vector4(0f, 0f, 0f, 0f));
			}

			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}

		static Color GetFinalColor(ref VisibleLight visibleLight)
		{
			Color color = visibleLight.finalColor;
			if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
			{
				color = color.linear;
			}
			return color;
		}

		void SetupDirectionalLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			dirLightColors[index] = GetFinalColor(ref visibleLight);
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = (float)light.renderingLayerMask;
			dirLightDirectionsAndMasks[index] = dirAndMask;
			dirLightShadowData[index] =
				shadows.ReserveDirectionalShadows(light, visibleIndex);

			// Cookie
			if (light.cookie != null)
			{
				Matrix4x4 worldToLight = visibleLight.localToWorldMatrix.inverse;
				// Ortho projection: map [-0.5, 0.5] to clip space, scale by cookie size
				float cookieSize = light.cookieSize;
				if (cookieSize <= 0f) cookieSize = 1f;
				float scale = 1f / cookieSize;
				Matrix4x4 ortho = Matrix4x4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, -0.5f, 0.5f);
				Matrix4x4 scaleM = Matrix4x4.Scale(new Vector3(scale, scale, 1));
				dirCookieMatrices[index] = ortho * scaleM * worldToLight;
				dirCookieEnabled[index] = 1f;
				dirCookieTextures[index] = light.cookie;
			}
			else
			{
				dirCookieEnabled[index] = 0f;
				dirCookieTextures[index] = null;
			}
		}

		void SetupPointLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			Vector4 color = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w =
				1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			Vector4 spotAngles = new Vector4(0f, 1f);
			Vector4 dirAndmask = Vector4.zero;
			dirAndmask.w = (float)light.renderingLayerMask;
			Vector4 shadowData = shadows.ReserveOtherShadows(light, visibleIndex);

			otherLightColors[index] = color;
			otherLightPositions[index] = position;
			otherLightSpotAngles[index] = spotAngles;
			otherLightDirectionsAndMasks[index] = dirAndmask;
			otherLightShadowData[index] = shadowData;
		}

		void SetupSpotLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			Vector4 color = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w =
				1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = light.renderingLayerMask;

			float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
			float outerCos = Mathf.Cos(
				Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
			float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
			Vector4 spotAngles = new Vector4(
				angleRangeInv, -outerCos * angleRangeInv
			);
			Vector4 shadowData = shadows.ReserveOtherShadows(
				light, visibleIndex);

			// Cookie
			Matrix4x4 cookieMatrix = Matrix4x4.identity;
			float cookieEnabled = 0f;
			Texture cookieTex = null;
			if (light.cookie != null)
			{
				Matrix4x4 worldToLight = visibleLight.localToWorldMatrix.inverse;
				float spotAngle = visibleLight.spotAngle;
				float range = visibleLight.range;
				Matrix4x4 persp = Matrix4x4.Perspective(spotAngle, 1f, 0.001f, range);
				// Cancel Unity's embedded Z-flip in perspective matrix
				persp.m22 = -persp.m22;
				cookieMatrix = persp * worldToLight;
				cookieEnabled = 1f;
				cookieTex = light.cookie;
			}

			otherLightColors[index] = color;
			otherLightPositions[index] = position;
			otherLightDirectionsAndMasks[index] = dirAndMask;
			otherLightSpotAngles[index] = spotAngles;
			otherLightShadowData[index] = shadowData;
			otherCookieMatrices[index] = cookieMatrix;
			otherCookieEnabled[index] = cookieEnabled;
			otherCookieTextures[index] = cookieTex;
		}

		public static void Dispose()
		{
			if (tileDataArray.IsCreated) tileDataArray.Dispose();
			if (tileLightArray.IsCreated) tileLightArray.Dispose();
			if (lightBoundsNative.IsCreated) lightBoundsNative.Dispose();
			if (tileDataTexture != null) Object.DestroyImmediate(tileDataTexture);
			if (tileLightTexture != null) Object.DestroyImmediate(tileLightTexture);
			tileDataBuffer?.Release();
			tileLightBuffer?.Release();
			lightBoundsBuffer?.Release();
			tileDataBufferSize = tileLightBufferSize = lightBoundsBufferSize = 0;
		}

		public static ShadowTextures Record(
			RenderGraph renderGraph,
			CullingResults cullingResults,Vector2Int attachmentSize, ShadowSettings shadowSettings,
			int renderingLayerMask, bool useForwardPlus)
		{
			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out LightingPass pass, sampler);
			builder.SetRenderFunc<LightingPass>(
				static (pass, context) => pass.Render(context));
			pass.Setup(cullingResults, attachmentSize,shadowSettings,
				 renderingLayerMask, useForwardPlus);
			builder.AllowPassCulling(false);
			return pass.shadows.GetRenderTextures(renderGraph, builder);
		}

		static readonly TextureFormat rgFormat = GetSupportedRGFormat();
		static readonly TextureFormat rFormat = GetSupportedSingleChannelFormat();

		static TextureFormat GetSupportedSingleChannelFormat()
		{
			if (SystemInfo.IsFormatSupported(GraphicsFormat.R32_SFloat, FormatUsage.Sample))
				return TextureFormat.RFloat;
			if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_SFloat, FormatUsage.Sample))
				return TextureFormat.RHalf;
			if (SystemInfo.IsFormatSupported(GraphicsFormat.R16_UNorm, FormatUsage.Sample))
				return TextureFormat.R16;
			return TextureFormat.R8;
		}

		static TextureFormat GetSupportedRGFormat()
		{
			if (SystemInfo.IsFormatSupported(GraphicsFormat.R32G32_SFloat, FormatUsage.Sample))
				return TextureFormat.RGFloat;
			if (SystemInfo.IsFormatSupported(GraphicsFormat.R16G16_SFloat, FormatUsage.Sample))
				return TextureFormat.RGHalf;
			return TextureFormat.RG16;
		}

		static readonly int maxTexSize = SystemInfo.maxTextureSize;

		static int NextPow2(int v)
		{
			if (v <= 1) return 1;
			v--;
			v |= v >> 1; v |= v >> 2; v |= v >> 4;
			v |= v >> 8; v |= v >> 16;
			return v + 1;
		}

		void EnsureComputeKernel()
		{
			if (cullKernel < 0 && CullComputeShader != null)
			{
				cullKernel = CullComputeShader.FindKernel("CullLights");
			}
		}

		void EnsureLightBoundsNative()
		{
			if (!lightBoundsNative.IsCreated)
				lightBoundsNative = new NativeArray<float4>(maxOtherLightCount, Allocator.Persistent);
		}

		void BuildTileLightListsJob(int tileCountTotal)
		{
			for (int i = 0; i < otherLightCount; i++)
				lightBoundsNative[i] = lightBounds[i];

			var job = new TileCullJob
			{
				lightBounds = lightBoundsNative,
				lightCount = otherLightCount,
				tileCount = new int2(tileCount.x, tileCount.y),
				dataStride = tileDataTexSize.x,
				screenUVToTileCoords = new float2(screenUVToTileCoordinates.x, screenUVToTileCoordinates.y),
				maxLightsPerTile = maxLightsPerTile,
				tileData = tileDataArray,
				tileLights = tileLightArray,
			};

			JobHandle handle = job.Schedule(tileCountTotal, 64);
			handle.Complete();

			SaveTileDataState();
		}

		void EnsureTileTextures(Vector2Int tileSize, int lightCount)
		{
			int dataW = Mathf.Min(NextPow2(Mathf.Max(tileSize.x, 1)), maxTexSize);
			int dataH = Mathf.Min(NextPow2(Mathf.Max(tileSize.y, 1)), maxTexSize);

			lightCount = Mathf.Max(lightCount, 1);
			int lightTexW = Mathf.Min(NextPow2(Mathf.CeilToInt(Mathf.Sqrt(lightCount))), maxTexSize);
			lightTexW = Mathf.Max(lightTexW, 1);
			int lightTexH = Mathf.Min(NextPow2(Mathf.CeilToInt(lightCount / (float)lightTexW)), maxTexSize);
			lightTexH = Mathf.Max(lightTexH, 1);

			// Always keep Texture2D alive as WebGL fallback (or when compute not supported)
			if (tileDataTexture == null || tileDataTexture.width < dataW || tileDataTexture.height < dataH)
			{
				if (tileDataTexture != null) Object.DestroyImmediate(tileDataTexture);
				tileDataTexture = new Texture2D(dataW, dataH, rgFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}
			if (tileLightTexture == null || tileLightTexture.width < lightTexW || tileLightTexture.height < lightTexH)
			{
				if (tileLightTexture != null) Object.DestroyImmediate(tileLightTexture);
				tileLightTexture = new Texture2D(lightTexW, lightTexH, rFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}

			tileDataTexSize = new Vector2Int(tileDataTexture.width, tileDataTexture.height);
			int actualDataTexSize = tileDataTexSize.x * tileDataTexSize.y;
			int actualLightTexSize = tileLightTexture.width * tileLightTexture.height;

			if (!tileDataArray.IsCreated || tileDataArray.Length < actualDataTexSize)
			{
				if (tileDataArray.IsCreated) tileDataArray.Dispose();
				tileDataArray = new NativeArray<float2>(actualDataTexSize, Allocator.Persistent);
			}
			if (!tileLightArray.IsCreated || tileLightArray.Length < actualLightTexSize)
			{
				if (tileLightArray.IsCreated) tileLightArray.Dispose();
				tileLightArray = new NativeArray<float>(actualLightTexSize, Allocator.Persistent);
			}

			if (isOpenGLES)
			{
				if (tileDataBufferSize < actualDataTexSize)
				{
					tileDataBuffer?.Release();
					tileDataBuffer = new ComputeBuffer(actualDataTexSize, 8); // Vector2 = 8 bytes
					tileDataBufferSize = actualDataTexSize;
				}
				if (tileLightBufferSize < actualLightTexSize)
				{
					tileLightBuffer?.Release();
					tileLightBuffer = new ComputeBuffer(actualLightTexSize, 4); // float = 4 bytes
					tileLightBufferSize = actualLightTexSize;
				}
				if (lightBoundsBufferSize < maxOtherLightCount)
				{
					lightBoundsBuffer?.Release();
					lightBoundsBuffer = new ComputeBuffer(maxOtherLightCount, 16); // float4 = 16 bytes
					lightBoundsBufferSize = maxOtherLightCount;
				}
			}
		}

		void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
		{
			Rect r = visibleLight.screenRect;
			lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
		}
	}
}
