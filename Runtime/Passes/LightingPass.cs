using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Unity.Mathematics;
using Object = UnityEngine.Object;

namespace TaoTie.RenderPipelines
{
	public class LightingPass
	{
		static readonly ProfilingSampler sampler = new("Lighting");

		private const int maxDirLightCount = 4, maxOtherLightCountOpenGLES2 = 8;
		
#if UNITY_WEBGL && !UNITY_EDITOR
		private const int maxOtherLightCount = 64;
#else
		private const int maxOtherLightCount = 256;
#endif

		// WebGL does not support ComputeBuffer; use Texture2D fallback on those platforms.
		static readonly bool useComputeBuffer = SystemInfo.supportsComputeShaders;

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

		static readonly Vector4[]
			forwardOtherLightColors = new Vector4[maxOtherLightCountOpenGLES2],
			forwardOtherLightPositions = new Vector4[maxOtherLightCountOpenGLES2],
			forwardOtherLightDirectionsAndMasks = new Vector4[maxOtherLightCountOpenGLES2],
			forwardOtherLightSpotAngles = new Vector4[maxOtherLightCountOpenGLES2],
			forwardOtherLightShadowData = new Vector4[maxOtherLightCountOpenGLES2];

		// Cookie support
		static readonly int
			dirCookieMatrixId = Shader.PropertyToID("_DirLightCookieMatrix"),
			otherCookieMatrixId = Shader.PropertyToID("_OtherLightCookieMatrix"),
			dirCookieEnabledId = Shader.PropertyToID("_DirLightCookieEnabled"),
			otherCookieEnabledId = Shader.PropertyToID("_OtherLightCookieEnabled");

		static readonly Matrix4x4[]
			dirCookieMatrices = new Matrix4x4[maxDirLightCount],
			otherCookieMatrices = new Matrix4x4[maxOtherLightCount],
			forwardOtherCookieMatrices = new Matrix4x4[maxOtherLightCountOpenGLES2];

		static readonly float[]
			dirCookieEnabled = new float[maxDirLightCount],
			otherCookieEnabled = new float[maxOtherLightCount],
			forwardOtherCookieEnabled = new float[maxOtherLightCountOpenGLES2];

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
		static readonly Texture[] forwardOtherCookieTextures = new Texture[maxOtherLightCountOpenGLES2];

		CullingResults cullingResults;

		readonly Shadows shadows = new();

		int dirLightCount, otherLightCount;

		Vector2 screenUVToTileCoordinates;
		Vector2Int tileCount;
		Vector2Int tileDataTexSize;
		int TileCount => tileCount.x * tileCount.y;

		static Vector4[] lightBounds = new Vector4[maxOtherLightCount];
		static NativeArray<Vector2> tileDataArray;
		static NativeArray<float> tileLightArray;
		private static Texture2D tileDataTexture;
		private static Texture2D tileLightTexture;
		private static ComputeBuffer tileDataBuffer, tileLightBuffer;
		static int tileDataBufferSize, tileLightBufferSize;

		// Light-centric tile culling temporaries
		static int[] tileLightCounts;
		static int[] tileLightFillPos;

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
						shadowSettings.other.tileSize <= 0 ? 64f : (float) shadowSettings.other.tileSize;
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
				if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 && max > maxOtherLightCountOpenGLES2)
				{
					max = maxOtherLightCountOpenGLES2;
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

				#if UNITY_EDITOR
				// In editor, always recompute and upload tile data per camera per frame
				BuildTileLightLists(tileCountTotal);
				tileDataDirty = true;
				#else
				// Dirty check: compare light bounds, count, and tile grid with last frame
				bool needsRecompute = TileDataNeedsRecompute(tileCountTotal);

				if (needsRecompute)
				{
					BuildTileLightLists(tileCountTotal);
					tileDataDirty = true;
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

		/// <summary>
		/// Light-centric tile culling: iterate lights, compute affected tile range,
		/// then fill tile data. Complexity O(lights × avgTilesPerLight) vs original
		/// O(tiles × lights).
		/// </summary>
		void BuildTileLightLists(int tileCountTotal)
		{
			int dataStride = tileDataTexSize.x;

			// Pass 1: clear per-tile light counts
			for (int j = 0; j < tileCountTotal; j++)
				tileLightCounts[j] = 0;

			// Pass 2: for each light, compute tile range and count
			for (int i = 0; i < otherLightCount; i++)
			{
				float4 b = lightBounds[i];
				int minTx = Mathf.Max(0, Mathf.CeilToInt(b.x * screenUVToTileCoordinates.x) - 1);
				int maxTx = Mathf.Min(tileCount.x - 1, Mathf.FloorToInt(b.z * screenUVToTileCoordinates.x));
				int minTy = Mathf.Max(0, Mathf.CeilToInt(b.y * screenUVToTileCoordinates.y) - 1);
				int maxTy = Mathf.Min(tileCount.y - 1, Mathf.FloorToInt(b.w * screenUVToTileCoordinates.y));

				for (int ty = minTy; ty <= maxTy; ty++)
				{
					for (int tx = minTx; tx <= maxTx; tx++)
					{
						int tileIdx = ty * tileCount.x + tx;
						if (tileLightCounts[tileIdx] < maxLightsPerTile)
							tileLightCounts[tileIdx]++;
					}
				}
			}

			// Pass 3: prefix sum → write headerIndex + count to tileDataArray
			int runningOffset = 0;
			for (int j = 0; j < tileCountTotal; j++)
			{
				int count = tileLightCounts[j];
				int y = j / tileCount.x;
				int x = j - y * tileCount.x;
				int texIndex = y * dataStride + x;
				tileDataArray[texIndex] = new Vector2(runningOffset, count);
				tileLightFillPos[j] = runningOffset;
				runningOffset += count;
			}

			// Pass 4: for each light, fill light indices into tileLightArray
			for (int i = 0; i < otherLightCount; i++)
			{
				float4 b = lightBounds[i];
				int minTx = Mathf.Max(0, Mathf.CeilToInt(b.x * screenUVToTileCoordinates.x) - 1);
				int maxTx = Mathf.Min(tileCount.x - 1, Mathf.FloorToInt(b.z * screenUVToTileCoordinates.x));
				int minTy = Mathf.Max(0, Mathf.CeilToInt(b.y * screenUVToTileCoordinates.y) - 1);
				int maxTy = Mathf.Min(tileCount.y - 1, Mathf.FloorToInt(b.w * screenUVToTileCoordinates.y));

				for (int ty = minTy; ty <= maxTy; ty++)
				{
					for (int tx = minTx; tx <= maxTx; tx++)
					{
						int tileIdx = ty * tileCount.x + tx;
						int count = tileLightCounts[tileIdx];
						int fillPos = tileLightFillPos[tileIdx];
						int headerIndex = (int)tileDataArray[ty * dataStride + tx].x;
						if (fillPos - headerIndex < count)
						{
							if (fillPos >= tileLightArray.Length)
							{
								Debug.LogError("请增加tileSize");
								break;
							}
							tileLightArray[fillPos] = i;
							tileLightFillPos[tileIdx]++;
						}
					}
				}
			}

			SaveTileDataState();
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
				if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2)
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
				else
				{
					buffer.SetGlobalVectorArray(otherLightColorsId, forwardOtherLightColors);
					buffer.SetGlobalVectorArray(
						otherLightPositionsId, forwardOtherLightPositions);
					buffer.SetGlobalVectorArray(
						otherLightDirectionsAndMasksId, forwardOtherLightDirectionsAndMasks);
					buffer.SetGlobalVectorArray(
						otherLightSpotAnglesId, forwardOtherLightSpotAngles);
					buffer.SetGlobalVectorArray(
						otherLightShadowDataId, forwardOtherLightShadowData);
				}
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

			if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2)
			{
				buffer.SetGlobalMatrixArray(otherCookieMatrixId, otherCookieMatrices);
				buffer.SetGlobalFloatArray(otherCookieEnabledId, otherCookieEnabled);
			}
			else
			{
				buffer.SetGlobalMatrixArray(otherCookieMatrixId, forwardOtherCookieMatrices);
				buffer.SetGlobalFloatArray(otherCookieEnabledId, forwardOtherCookieEnabled);
			}
			for (int ci = 0; ci < maxOtherLightCountOpenGLES2; ci++)
			{
				Texture tex = useForwardPlus ? otherCookieTextures[ci] : forwardOtherCookieTextures[ci];
				buffer.SetGlobalTexture(otherCookieTexIDs[ci],
					tex != null ? tex : whiteCookieTexture);
			}

			shadows.Render(context);

			if (useForwardPlus)
			{
				bool canUseBuffer = useComputeBuffer && tileDataBuffer != null && tileLightBuffer != null;
				if (tileDataDirty)
				{
					if (canUseBuffer)
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

			if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2)
			{
				otherLightColors[index] = color;
				otherLightPositions[index] = position;
				otherLightSpotAngles[index] = spotAngles;
				otherLightDirectionsAndMasks[index] = dirAndmask;
				otherLightShadowData[index] = shadowData;
			}
			else
			{
				forwardOtherLightColors[index] = color;
				forwardOtherLightPositions[index] = position;
				forwardOtherLightSpotAngles[index] = spotAngles;
				forwardOtherLightDirectionsAndMasks[index] = dirAndmask;
				forwardOtherLightShadowData[index] = shadowData;
			}
		}

		void SetupSpotLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			Vector4 color = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w =
				1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = (float)light.renderingLayerMask;

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

			if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2)
			{
				otherLightColors[index] = color;
				otherLightPositions[index] = position;
				otherLightDirectionsAndMasks[index] = dirAndMask;
				otherLightSpotAngles[index] = spotAngles;
				otherLightShadowData[index] = shadowData;
				otherCookieMatrices[index] = cookieMatrix;
				otherCookieEnabled[index] = cookieEnabled;
				otherCookieTextures[index] = cookieTex;
			}
			else
			{
				forwardOtherLightColors[index] = color;
				forwardOtherLightPositions[index] = position;
				forwardOtherLightDirectionsAndMasks[index] = dirAndMask;
				forwardOtherLightSpotAngles[index] = spotAngles;
				forwardOtherLightShadowData[index] = shadowData;
				forwardOtherCookieMatrices[index] = cookieMatrix;
				forwardOtherCookieEnabled[index] = cookieEnabled;
				forwardOtherCookieTextures[index] = cookieTex;
			}
		}

		public static void Dispose()
		{
			if (tileDataArray.IsCreated) tileDataArray.Dispose();
			if (tileLightArray.IsCreated) tileLightArray.Dispose();
			if (tileDataTexture != null) Object.DestroyImmediate(tileDataTexture);
			if (tileLightTexture != null) Object.DestroyImmediate(tileLightTexture);
			tileDataBuffer?.Release();
			tileLightBuffer?.Release();
			tileDataBufferSize = tileLightBufferSize = 0;
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
				tileDataArray = new NativeArray<Vector2>(actualDataTexSize, Allocator.Persistent);
			}
			if (!tileLightArray.IsCreated || tileLightArray.Length < actualLightTexSize)
			{
				if (tileLightArray.IsCreated) tileLightArray.Dispose();
				tileLightArray = new NativeArray<float>(actualLightTexSize, Allocator.Persistent);
			}

			int tileCountTotal = TileCount;
			if (tileLightCounts == null || tileLightCounts.Length < tileCountTotal)
			{
				tileLightCounts = new int[tileCountTotal];
				tileLightFillPos = new int[tileCountTotal];
			}

			if (useComputeBuffer)
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
			}
		}

		void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
		{
			Rect r = visibleLight.screenRect;
			lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
		}
	}
}
