using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
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
			GraphicsDeviceType.OpenGLES3 => 32,
			_ => 256,
		};

		static int effectiveWordsPerTile => Mathf.CeilToInt(maxOtherLightCount / 32f);

		private static readonly bool notOpenGLES = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 &&
		                                           SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3 &&
		                                           SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore;
		

		private static readonly int
			dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
			dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
			dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
			dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
			tileBitmaskTexId = Shader.PropertyToID("_ForwardPlusTileBitmaskTex"),
			tileBitmaskBufId = Shader.PropertyToID("_ForwardPlusTileBitmaskBuf"),
			zBinTexId = Shader.PropertyToID("_ForwardPlusZBinTex"),
			zBinBufId = Shader.PropertyToID("_ForwardPlusZBinBuf"),
			tileSettingsId = Shader.PropertyToID("_ForwardPlusTileSettings"),
			dataSizeId = Shader.PropertyToID("_ForwardPlusDataSize"),
			zBinParamsId = Shader.PropertyToID("_ZBinParams"),
			depthTexID = Shader.PropertyToID("_CameraDepthTexture");

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

		static Vector4[]
			otherLightColors = new Vector4[maxOtherLightCount],
			otherLightPositions = new Vector4[maxOtherLightCount],
			otherLightDirectionsAndMasks = new Vector4[maxOtherLightCount],
			otherLightSpotAngles = new Vector4[maxOtherLightCount],
			otherLightShadowData = new Vector4[maxOtherLightCount];

		// _VertexLightCount = total (pixel + vertex) lights in shared _OtherLight arrays
		static int totalLightCount;
		static readonly int vertexLightCountId = Shader.PropertyToID("_VertexLightCount");

		// Candidate classification arrays (reused per frame)
		static int[] s_pixelCandidates, s_vertexCandidates;
		static float[] s_pixelScores, s_vertexScores;
		static int s_pixelCandidateCount, s_vertexCandidateCount;

		static readonly int
			dirCookieMatrixId = Shader.PropertyToID("_DirLightCookieMatrix"),
			otherCookieMatrixId = Shader.PropertyToID("_OtherLightCookieMatrix"),
			dirCookieEnabledId = Shader.PropertyToID("_DirLightCookieEnabled"),
			otherCookieEnabledId = Shader.PropertyToID("_OtherLightCookieEnabled");

		const int maxCookieOtherLightCount = 8;

		static readonly Matrix4x4[]
			dirCookieMatrices = new Matrix4x4[maxDirLightCount],
			otherCookieMatrices = new Matrix4x4[maxCookieOtherLightCount];

		static readonly float[]
			dirCookieEnabled = new float[maxDirLightCount],
			otherCookieEnabled = new float[maxCookieOtherLightCount];

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
		
		static int activeCookieCount;
		static readonly Texture[] dirCookieTextures = new Texture[maxDirLightCount];
		static readonly Texture[] otherCookieTextures = new Texture[maxCookieOtherLightCount];

		static float RenderLayerMaskToFloat(int mask)
		{
			// 0x7FFFFFFF (Everything) = NaN as asfloat → GPU undefined.
			// Use 0x00FFFFFF sentinel; HLSL treats it as all-layers-match.
			// All other values use (float) value cast — produces normal (non-denorm)
			// floats that survive CBUFFER without flushing. Powers of 2 (single layers)
			// are always exact in float32.
			if (mask == 0x7FFFFFFF)
				return (float)0x00FFFFFF;
			return (float)mask;
		}

		CullingResults cullingResults;
		readonly Shadows shadows = new();

		int dirLightCount, otherLightCount;

		// Static tile state 閳?accessible from ForwardPlusCullPass
		static Vector2 s_screenUVToTileCoords;
		static Vector2Int s_tileCount;
		static Vector2Int s_tileDataTexSize;
		static int s_otherLightCount;
		static int s_zBinCount;
		static int s_maxLightsPerTile;
		static float s_cameraNear, s_cameraFar;
		static bool s_useForwardPlus;
		static bool s_useDeferred;
		static bool s_useDepth25D;
		static Vector2Int s_screenSize;
		static int TileCount => s_tileCount.x * s_tileCount.y;

		static Vector4[] lightBounds = new Vector4[maxOtherLightCount];

		// Static arrays for light priority selection (avoid per-frame allocation)
		static int[] s_candidateIndices;
		static NativeArray<uint> tileBitmaskArray;
		private static Texture2D tileBitmaskTexture;
		private static ComputeBuffer tileBitmaskBuffer;
		static int tileBitmaskBufferSize;
		static NativeArray<uint> zBinData;
		private static Texture2D zBinTexture;
		private static ComputeBuffer zBinBuffer;
		static int zBinBufferSize;
		static NativeArray<float2> lightZRangesNative;
		static ComputeBuffer lightZRangesBuffer;
		static int lightZRangesBufferSize;
		public static ComputeShader CullComputeShader { get; set; }
		static int cullKernel = -1;
		static bool cullKernelChecked = false;
		static ComputeBuffer lightBoundsBuffer;
		static int lightBoundsBufferSize;
		static Texture2D dummyTexture;

		static void EnsureDummyTexture()
		{
			if (dummyTexture == null)
			{
				dummyTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				dummyTexture.SetPixel(0, 0, Color.white);
				dummyTexture.Apply(false);
				dummyTexture.filterMode = FilterMode.Point;
				dummyTexture.wrapMode = TextureWrapMode.Clamp;
				dummyTexture.name = "Dummy";
			}
		}
		static NativeArray<float4> lightBoundsNative;
		static bool tileDataDirty = true;
		Matrix4x4 viewMatrix;
		Vector3 cameraPosition;

		public void Setup(
			CullingResults cullingResults, Vector2Int attachmentSize,
			ShadowSettings shadowSettings, int renderingLayerMask, bool useForwardPlus, bool useDeferred,
			bool useDepth25D, Camera camera)
		{
			this.cullingResults = cullingResults;
			shadows.Setup(cullingResults, shadowSettings);
			s_useForwardPlus = useForwardPlus;
			s_useDeferred = useDeferred;
			s_useDepth25D = useDepth25D;
			if (useForwardPlus)
			{
				s_maxLightsPerTile = shadowSettings.other.maxLightsPerTile;
				s_zBinCount = shadowSettings.other.zBinCount;
				viewMatrix = camera.worldToCameraMatrix;
				s_viewMatrix = viewMatrix;
				s_cameraNear = camera.nearClipPlane;
				s_cameraFar = camera.farClipPlane;
				cameraPosition = camera.transform.position;
				s_screenSize = new Vector2Int(attachmentSize.x, attachmentSize.y);
				float tileScreenPixelSize = (float) ShadowSettings.Other.TileSize.Off;
				if (s_maxLightsPerTile > 0)
				{
					tileScreenPixelSize =
						shadowSettings.other.tileSize <= 0 ? 32f : (float) shadowSettings.other.tileSize;
#if UNITY_WEBGL && !UNITY_EDITOR
					tileScreenPixelSize = Mathf.Max(32f, tileScreenPixelSize);
#else
					if (!notOpenGLES) tileScreenPixelSize = Mathf.Max(32f, tileScreenPixelSize);
#endif
				}

				// Bitmask makes memory cost fixed (wordsPerTile * sizeof(uint) per tile),
				// so smaller tiles give tighter culling without memory penalty.
				// Auto-scale down for high resolutions but cap at user tile size (no upscale).
				float scaleFactor = Mathf.Pow(2,
					Mathf.FloorToInt(Mathf.Sqrt((attachmentSize.x / 1366f) * (attachmentSize.y / 768f)) - 0.1f));
				tileScreenPixelSize = Mathf.Max(tileScreenPixelSize, tileScreenPixelSize / scaleFactor);
				s_screenUVToTileCoords.x = attachmentSize.x / tileScreenPixelSize;
				s_screenUVToTileCoords.y = attachmentSize.y / tileScreenPixelSize;
				s_tileCount.x = Mathf.CeilToInt(s_screenUVToTileCoords.x);
				s_tileCount.y = Mathf.CeilToInt(s_screenUVToTileCoords.y);
			}

			SetupLights(renderingLayerMask, shadowSettings);
		}

		void SetupLights(int renderingLayerMask, ShadowSettings shadowSettings)
		{
			NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
			int i;
			dirLightCount = otherLightCount = 0;
			totalLightCount = 0;
			activeCookieCount = 0;
			s_otherLightCount = 0;
			s_pixelCandidateCount = 0;
			s_vertexCandidateCount = 0;

			// Ensure candidate arrays
			int visCount = visibleLights.Length;
			if (s_candidateIndices == null || s_candidateIndices.Length < visCount)
			{
				s_candidateIndices = new int[visCount];
				s_pixelCandidates = new int[visCount];
				s_pixelScores = new float[visCount];
				s_vertexCandidates = new int[visCount];
				s_vertexScores = new float[visCount];
			}

			// First pass: process Directional lights + collect Other light candidates
			// Classified by Light.renderMode: Important→pixel, NotImportant→vertex, Auto→pixel
			for (i = 0; i < visibleLights.Length; i++)
			{
				VisibleLight visibleLight = visibleLights[i];
				Light light = visibleLight.light;
				if ((light.renderingLayerMask & renderingLayerMask) != 0)
				{
					switch (visibleLight.lightType)
					{
						case LightType.Directional:
							if (dirLightCount < maxDirLightCount &&
							    dirLightCount < shadowSettings.directional.maxLightCount)
							{
								SetupDirectionalLight(
									dirLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Point:
						case LightType.Spot:
							Color c = visibleLight.finalColor;
							float brightness = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
							Rect r = visibleLight.screenRect;
							float screenArea = r.width * r.height;
							Vector4 lightPos4 = visibleLight.localToWorldMatrix.GetColumn(3);
							Vector3 lightPos = new(lightPos4.x, lightPos4.y, lightPos4.z);
							float distSqr = Vector3.SqrMagnitude(lightPos - cameraPosition);
							float score = brightness * screenArea / Mathf.Max(distSqr, 1f);

							// Deferred: all lights go to pixel path (no vertex lights)
							if (s_useDeferred)
							{
								s_pixelCandidates[s_pixelCandidateCount] = i;
								s_pixelScores[s_pixelCandidateCount] = score;
								s_pixelCandidateCount++;
							}
							else if (light.renderMode == LightRenderMode.ForceVertex)
							{
								s_vertexCandidates[s_vertexCandidateCount] = i;
								s_vertexScores[s_vertexCandidateCount] = score;
								s_vertexCandidateCount++;
							}
							else
							{
								// ForcePixel and Auto both start as pixel candidates
								s_pixelCandidates[s_pixelCandidateCount] = i;
								s_pixelScores[s_pixelCandidateCount] = score;
								// Boost ForcePixel lights so they sort higher and are never demoted
								if (light.renderMode == LightRenderMode.ForcePixel)
									s_pixelScores[s_pixelCandidateCount] += 1e9f;
								s_pixelCandidateCount++;
							}
							break;
					}
				}
			}

			int max;
			if (s_useForwardPlus)
			{
				max = s_maxLightsPerTile * TileCount;
				if (maxOtherLightCount < max)
					max = maxOtherLightCount;
			}
			else if (s_useDeferred)
			{
				// Deferred: all lights are per-pixel, no vertex lights, no cap
				max = s_pixelCandidateCount;
			}
			else
			{
				max = shadowSettings.maxOtherLights;
				if (max > maxOtherLightCount)
					max = maxOtherLightCount;
			}

			// Ensure capacity for pixel + vertex lights (shared arrays)
			EnsureOtherLightArrayCapacity(Mathf.Min(maxOtherLightCount, s_pixelCandidateCount + s_vertexCandidateCount));

			// Sort pixel candidates by score (descending)
			int pixelLimit = Mathf.Min(s_pixelCandidateCount, max);
			if (s_pixelCandidateCount > max)
			{
				for (int a = 0; a < max; a++)
				{
					int best = a;
					for (int b = a + 1; b < s_pixelCandidateCount; b++)
					{
						if (s_pixelScores[b] > s_pixelScores[best])
							best = b;
					}
					if (best != a)
					{
						(s_pixelScores[a], s_pixelScores[best]) = (s_pixelScores[best], s_pixelScores[a]);
						(s_pixelCandidates[a], s_pixelCandidates[best]) = (s_pixelCandidates[best], s_pixelCandidates[a]);
					}
				}
				// Demoted pixel lights (beyond pixelLimit) → vertex candidates (skip in Deferred: no vertex lights)
				if (!s_useDeferred)
				{
					for (int a = pixelLimit; a < s_pixelCandidateCount; a++)
					{
						s_vertexCandidates[s_vertexCandidateCount] = s_pixelCandidates[a];
						// Boost demoted pixel lights above ForceVertex: ForcePixel (+2e9f) > Auto (+1e9f) > ForceVertex (0)
						s_vertexScores[s_vertexCandidateCount] = s_pixelScores[a] % 1e9f + 1e9f;
						s_vertexCandidateCount++;
					}
				}
			}

			// Setup pixel lights
			for (i = 0; i < pixelLimit; i++)
			{
				int vi = s_pixelCandidates[i];
				VisibleLight visibleLight = visibleLights[vi];
				Light light = visibleLight.light;
				if (s_useForwardPlus)
					SetupForwardPlus(otherLightCount, ref visibleLight);
				if (visibleLight.lightType == LightType.Point)
					SetupPointLight(otherLightCount, vi, ref visibleLight, light);
				else
					SetupSpotLight(otherLightCount, vi, ref visibleLight, light);
				otherLightCount++;
			}
			s_otherLightCount = otherLightCount;

			// Sort vertex candidates by score (descending)
			int vertexMax = Mathf.Min(s_vertexCandidateCount, maxOtherLightCount - otherLightCount);
			if (s_vertexCandidateCount > 1)
			{
				int sortEnd = Mathf.Min(vertexMax, s_vertexCandidateCount);
				for (int a = 0; a < sortEnd; a++)
				{
					int best = a;
					for (int b = a + 1; b < s_vertexCandidateCount; b++)
					{
						if (s_vertexScores[b] > s_vertexScores[best])
							best = b;
					}
					if (best != a)
					{
						(s_vertexScores[a], s_vertexScores[best]) = (s_vertexScores[best], s_vertexScores[a]);
						(s_vertexCandidates[a], s_vertexCandidates[best]) = (s_vertexCandidates[best], s_vertexCandidates[a]);
					}
				}
			}

			// Setup vertex lights (appended to same _OtherLight arrays after pixel lights)
			int vertexCount = 0;
			for (i = 0; i < vertexMax; i++)
			{
				int vi = s_vertexCandidates[i];
				VisibleLight visibleLight = visibleLights[vi];
				Light light = visibleLight.light;
				int idx = otherLightCount + vertexCount;
				if (visibleLight.lightType == LightType.Point)
					SetupVertexPointLight(idx, ref visibleLight, light);
				else
					SetupVertexSpotLight(idx, ref visibleLight, light);
				vertexCount++;
			}
			totalLightCount = otherLightCount + vertexCount;

			if (s_useForwardPlus)
			{
				int tileCountTotal = TileCount;
				EnsureTileBuffers(s_tileCount);
				EnsureComputeKernel();
				EnsureLightBoundsNative();
				EnsureLightZRangesNative();
				ComputeLightZRanges();
				BuildZBinData();
				bool useGpuCompute = notOpenGLES && CullComputeShader != null && cullKernel >= 0;
				if (!useGpuCompute)
					BuildTileBitmaskJob(tileCountTotal);
				tileDataDirty = true;
			}
		}

		static void ComputeLightZRanges()
		{
			for (int i = 0; i < s_otherLightCount; i++)
			{
				Vector3 lightWorldPos = new(
					otherLightPositions[i].x,
					otherLightPositions[i].y,
					otherLightPositions[i].z);
				Vector3 camPos = s_viewMatrix.MultiplyPoint(lightWorldPos);
				float centerZ = -camPos.z;
				float range = Mathf.Sqrt(1f / Mathf.Max(otherLightPositions[i].w, 0.00001f));
				lightZRangesNative[i] = new float2(
					Mathf.Max(centerZ - range, s_cameraNear),
					Mathf.Min(centerZ + range, s_cameraFar));
			}
		}

		static Matrix4x4 s_viewMatrix;

		void BuildZBinData()
		{
			int wpt = effectiveWordsPerTile;
			int zBinTotal = s_zBinCount * wpt;
			if (!zBinData.IsCreated || zBinData.Length < zBinTotal)
			{
				if (zBinData.IsCreated) zBinData.Dispose();
				zBinData = new NativeArray<uint>(zBinTotal, Allocator.Persistent);
			}
			unsafe
			{
				UnsafeUtility.MemSet(zBinData.GetUnsafePtr(), 0, zBinTotal * sizeof(uint));
			}
			float invDepthRange = 1f / Mathf.Max(s_cameraFar - s_cameraNear, 0.0001f);
			for (int i = 0; i < s_otherLightCount; i++)
			{
				int binMin = Mathf.Clamp(
					Mathf.FloorToInt((lightZRangesNative[i].x - s_cameraNear) * invDepthRange * s_zBinCount),
					0, s_zBinCount - 1);
				int binMax = Mathf.Clamp(
					Mathf.CeilToInt((lightZRangesNative[i].y - s_cameraNear) * invDepthRange * s_zBinCount),
					0, s_zBinCount - 1);
				int wordIdx = i / 32;
				uint bitMask = 1u << (i % 32);
				for (int b = binMin; b <= binMax; b++)
					zBinData[b * wpt + wordIdx] |= bitMask;
			}
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
			buffer.SetGlobalFloat(vertexLightCountId, totalLightCount);
			if (totalLightCount > 0)
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

			if (activeCookieCount > 0)
			{
				EnsureDummyTexture();
				buffer.SetGlobalMatrixArray(dirCookieMatrixId, dirCookieMatrices);
				buffer.SetGlobalFloatArray(dirCookieEnabledId, dirCookieEnabled);
				for (int ci = 0; ci < maxDirLightCount; ci++)
					buffer.SetGlobalTexture(dirCookieTexIDs[ci],
						dirCookieTextures[ci] != null ? dirCookieTextures[ci] : dummyTexture);
				buffer.SetGlobalMatrixArray(otherCookieMatrixId, otherCookieMatrices);
				buffer.SetGlobalFloatArray(otherCookieEnabledId, otherCookieEnabled);
				for (int ci = 0; ci < otherCookieTexIDs.Length; ci++)
				{
					buffer.SetGlobalTexture(otherCookieTexIDs[ci],
						otherCookieTextures[ci] != null ? otherCookieTextures[ci] : dummyTexture);
				}
			}

			shadows.Render(context);
			if (!s_useForwardPlus)
				buffer.SetGlobalVector(tileSettingsId, new Vector4(0f, 0f, 0f, 0f));
			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}

		/// <summary>
		/// Dispatch Forward+ compute culling and set global tile/ZBin buffers.
		/// Called by ForwardPlusCullPass after depth texture is available.
		/// Compute shader reads _CameraDepthTexture global (set by DepthPrePass).
		/// </summary>
		public static void RenderForwardPlusCull(CommandBuffer buffer)
		{
			if (!s_useForwardPlus) return;
			bool canUseBuffer = notOpenGLES && tileBitmaskBuffer != null;
			bool useGpuCompute = canUseBuffer && CullComputeShader != null && cullKernel >= 0;
			if (tileDataDirty)
			{
				if (useGpuCompute)
				{
					lightBoundsBuffer.SetData(lightBounds, 0, 0, s_otherLightCount);
					lightZRangesBuffer.SetData(lightZRangesNative, 0, 0, s_otherLightCount);
					CullComputeShader.SetBuffer(cullKernel, "_LightBounds", lightBoundsBuffer);
					CullComputeShader.SetBuffer(cullKernel, "_LightZRanges", lightZRangesBuffer);
					CullComputeShader.SetBuffer(cullKernel, "_TileBitmask", tileBitmaskBuffer);
					CullComputeShader.SetInt("_LightCount", s_otherLightCount);
					CullComputeShader.SetVector("_TileCount", new Vector4(
						s_tileCount.x, s_tileCount.y, s_tileDataTexSize.x, effectiveWordsPerTile));
					CullComputeShader.SetVector("_ScreenUVToTileCoords", s_screenUVToTileCoords);
					CullComputeShader.SetVector("_ScreenSize", new Vector2(s_screenSize.x, s_screenSize.y));
					// Set depth texture for 2.5D culling (only when DepthPrePass is available)
					CullComputeShader.SetInt("_UseDepth25D", s_useDepth25D ? 1 : 0);
					
					Texture depthTex = Shader.GetGlobalTexture(depthTexID);
					if (depthTex == null)
					{
						EnsureDummyTexture();
						depthTex = dummyTexture;
					}
					CullComputeShader.SetTexture(cullKernel, "_DepthTexture", depthTex);
					float n = s_cameraNear;
					float f = s_cameraFar;
					float fn = f * n;
					float range = f - n;
					Vector4 zBufferParams = SystemInfo.usesReversedZBuffer
						? new Vector4(-1 + f / n, 1, range / fn, 1 / f)
						: new Vector4(1 - f / n, f / n, -range / fn, 1 / n);
					CullComputeShader.SetVector("_DepthZBufferParams", zBufferParams);

					// Clear bitmask before per-light dispatch (InterlockedOr needs zeroed buffer)
					unsafe
					{
						UnsafeUtility.MemSet(
							tileBitmaskArray.GetUnsafePtr(), 0,
							tileBitmaskArray.Length * sizeof(uint));
					}
					tileBitmaskBuffer.SetData(tileBitmaskArray);

					// Per-light dispatch: each thread handles one light
					int lightGroups = Mathf.Max(1, Mathf.CeilToInt(s_otherLightCount / 64f));
					buffer.DispatchCompute(CullComputeShader, cullKernel, lightGroups, 1, 1);
				}
				else if (canUseBuffer)
				{
					tileBitmaskBuffer.SetData(tileBitmaskArray);
				}
				else
				{
					tileBitmaskTexture.SetPixelData(tileBitmaskArray.Reinterpret<float>(), 0);
					tileBitmaskTexture.Apply(false);
				}

				if (canUseBuffer && zBinBuffer != null)
					zBinBuffer.SetData(zBinData);
				else if (zBinTexture != null)
				{
					zBinTexture.SetPixelData(zBinData.Reinterpret<float>(), 0);
					zBinTexture.Apply(false);
				}

				tileDataDirty = false;
			}

			if (canUseBuffer)
			{
				buffer.SetGlobalBuffer(tileBitmaskBufId, tileBitmaskBuffer);
				buffer.SetGlobalBuffer(zBinBufId, zBinBuffer);
			}
			else
			{
				buffer.SetGlobalTexture(tileBitmaskTexId, tileBitmaskTexture);
				buffer.SetGlobalTexture(zBinTexId, zBinTexture);
			}

			buffer.SetGlobalVector(dataSizeId, new Vector4(
				s_tileDataTexSize.x, s_zBinCount, effectiveWordsPerTile, 0));
			buffer.SetGlobalVector(zBinParamsId, new Vector4(
				s_zBinCount, s_cameraNear,
				1f / Mathf.Max(s_cameraFar - s_cameraNear, 0.0001f),
				s_cameraFar));
			buffer.SetGlobalVector(tileSettingsId, new Vector4(
				s_screenUVToTileCoords.x, s_screenUVToTileCoords.y,
				s_tileCount.x,
				effectiveWordsPerTile));
		}

		static Color GetFinalColor(ref VisibleLight visibleLight)
		{
			Color color = visibleLight.finalColor;
			if (QualitySettings.activeColorSpace == ColorSpace.Gamma)
				color = color.linear;
			return color;
		}

		void SetupDirectionalLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			dirLightColors[index] = GetFinalColor(ref visibleLight);
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = RenderLayerMaskToFloat(light.renderingLayerMask);
			dirLightDirectionsAndMasks[index] = dirAndMask;
			dirLightShadowData[index] =
				shadows.ReserveDirectionalShadows(light, visibleIndex);
			if (light.cookie != null)
			{
				Matrix4x4 worldToLight = visibleLight.localToWorldMatrix.inverse;
				float cookieSize = light.cookieSize;
				if (cookieSize <= 0f) cookieSize = 1f;
				float scale = 1f / cookieSize;
				Matrix4x4 ortho = Matrix4x4.Ortho(-0.5f, 0.5f, -0.5f, 0.5f, -0.5f, 0.5f);
				Matrix4x4 scaleM = Matrix4x4.Scale(new Vector3(scale, scale, 1));
				dirCookieMatrices[index] = ortho * scaleM * worldToLight;
				dirCookieEnabled[index] = 1f;
				activeCookieCount++;
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
			dirAndmask.w = RenderLayerMaskToFloat(light.renderingLayerMask);
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
			dirAndMask.w = RenderLayerMaskToFloat(light.renderingLayerMask);
			float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
			float outerCos = Mathf.Cos(
				Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
			float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
			Vector4 spotAngles = new Vector4(
				angleRangeInv, -outerCos * angleRangeInv
			);
			Vector4 shadowData = shadows.ReserveOtherShadows(
				light, visibleIndex);
			Matrix4x4 cookieMatrix = Matrix4x4.identity;
			float cookieEnabled = 0f;
			Texture cookieTex = null;
			if (light.cookie != null)
			{
				Matrix4x4 worldToLight = visibleLight.localToWorldMatrix.inverse;
				float spotAngle = visibleLight.spotAngle;
				float range = visibleLight.range;
				Matrix4x4 persp = Matrix4x4.Perspective(spotAngle, 1f, 0.001f, range);
				persp.m22 = -persp.m22;
				cookieMatrix = persp * worldToLight;
				cookieEnabled = 1f;
				activeCookieCount++;
				cookieTex = light.cookie;
			}

			otherLightColors[index] = color;
			otherLightPositions[index] = position;
			otherLightDirectionsAndMasks[index] = dirAndMask;
			otherLightSpotAngles[index] = spotAngles;
			otherLightShadowData[index] = shadowData;
			if (index < maxCookieOtherLightCount)
			{
				otherCookieMatrices[index] = cookieMatrix;
				otherCookieEnabled[index] = cookieEnabled;
				otherCookieTextures[index] = cookieTex;
			}
		}

		void SetupVertexPointLight(int index, ref VisibleLight visibleLight, Light light)
		{
			Vector4 color = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			otherLightColors[index] = color;
			otherLightPositions[index] = position;
			otherLightDirectionsAndMasks[index] = Vector4.zero;
			otherLightSpotAngles[index] = new Vector4(0f, 1f);
			otherLightShadowData[index] = Vector4.zero;
		}

		void SetupVertexSpotLight(int index, ref VisibleLight visibleLight, Light light)
		{
			Vector4 color = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w = 1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = RenderLayerMaskToFloat(light.renderingLayerMask);
			float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
			float outerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
			float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
			Vector4 spotAngles = new Vector4(angleRangeInv, -outerCos * angleRangeInv);
			otherLightColors[index] = color;
			otherLightPositions[index] = position;
			otherLightDirectionsAndMasks[index] = dirAndMask;
			otherLightSpotAngles[index] = spotAngles;
			otherLightShadowData[index] = Vector4.zero;
		}

		public static void Dispose()
		{
			if (tileBitmaskArray.IsCreated) tileBitmaskArray.Dispose();
			if (zBinData.IsCreated) zBinData.Dispose();
			if (lightBoundsNative.IsCreated) lightBoundsNative.Dispose();
			if (lightZRangesNative.IsCreated) lightZRangesNative.Dispose();
			if (tileBitmaskTexture != null) Object.DestroyImmediate(tileBitmaskTexture);
			if (zBinTexture != null) Object.DestroyImmediate(zBinTexture);
			if (dummyTexture != null) Object.DestroyImmediate(dummyTexture);
			tileBitmaskBuffer?.Release();
			zBinBuffer?.Release();
			lightBoundsBuffer?.Release();
			lightZRangesBuffer?.Release();
			tileBitmaskBufferSize = zBinBufferSize = lightBoundsBufferSize = lightZRangesBufferSize = 0;
			cullKernel = -1;
			cullKernelChecked = false;
		}

		public static ShadowTextures Record(
			RenderGraph renderGraph,
			CullingResults cullingResults, Vector2Int attachmentSize, ShadowSettings shadowSettings,
			int renderingLayerMask, bool useForwardPlus, bool useDeferred, bool useDepth25D, Camera camera)
		{
			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out LightingPass pass, sampler);
			builder.SetRenderFunc<LightingPass>(
				static (pass, context) => pass.Render(context));
			pass.Setup(cullingResults, attachmentSize, shadowSettings,
				renderingLayerMask, useForwardPlus,useDeferred, useDepth25D, camera);
			builder.AllowPassCulling(false);
			return pass.shadows.GetRenderTextures(renderGraph, builder);
		}

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

		static readonly int maxTexSize = SystemInfo.maxTextureSize;

		static int NextPow2(int v)
		{
			if (v <= 1) return 1;
			v--;
			v |= v >> 1;
			v |= v >> 2;
			v |= v >> 4;
			v |= v >> 8;
			v |= v >> 16;
			return v + 1;
		}

		void EnsureComputeKernel()
		{
			if (cullKernelChecked) return;
			cullKernelChecked = true;
#if !UNITY_WEBGL || UNITY_EDITOR
			if (CullComputeShader != null && SystemInfo.supportsComputeShaders &&
			    CullComputeShader.HasKernel("CullLights"))
			{
				cullKernel = CullComputeShader.FindKernel("CullLights");
			}
#endif
		}

		static void EnsureOtherLightArrayCapacity(int requiredSize)
		{
			if (otherLightColors.Length >= requiredSize) return;
			int newSize = Mathf.NextPowerOfTwo(requiredSize);
			Array.Resize(ref otherLightColors, newSize);
			Array.Resize(ref otherLightPositions, newSize);
			Array.Resize(ref otherLightDirectionsAndMasks, newSize);
			Array.Resize(ref otherLightSpotAngles, newSize);
			Array.Resize(ref otherLightShadowData, newSize);
			Array.Resize(ref lightBounds, newSize);
		}

		void EnsureLightBoundsNative()
		{
			int size = maxOtherLightCount;
			if (!lightBoundsNative.IsCreated || lightBoundsNative.Length < size)
			{
				if (lightBoundsNative.IsCreated) lightBoundsNative.Dispose();
				lightBoundsNative = new NativeArray<float4>(size, Allocator.Persistent);
			}
		}

		void EnsureLightZRangesNative()
		{
			int size = maxOtherLightCount;
			if (!lightZRangesNative.IsCreated || lightZRangesNative.Length < size)
			{
				if (lightZRangesNative.IsCreated) lightZRangesNative.Dispose();
				lightZRangesNative = new NativeArray<float2>(size, Allocator.Persistent);
			}
		}

		void BuildTileBitmaskJob(int tileCountTotal)
		{
			for (int i = 0; i < s_otherLightCount; i++)
				lightBoundsNative[i] = lightBounds[i];
			unsafe
			{
				UnsafeUtility.MemSet(
					tileBitmaskArray.GetUnsafePtr(), 0,
					tileBitmaskArray.Length * sizeof(uint));
			}

			var job = new TileCullJob
			{
				lightBounds = lightBoundsNative,
				lightCount = s_otherLightCount,
				tileCount = new int2(s_tileCount.x, s_tileCount.y),
				dataStride = s_tileDataTexSize.x,
				screenUVToTileCoords = new float2(s_screenUVToTileCoords.x, s_screenUVToTileCoords.y),
				wordsPerTile = effectiveWordsPerTile,
				tileBitmask = tileBitmaskArray,
			};
			JobHandle handle = job.Schedule(s_otherLightCount, 64);
			handle.Complete();
		}

		void EnsureTileBuffers(Vector2Int tileSize)
		{
			int wpt = effectiveWordsPerTile;
			int dataW = Mathf.Min(NextPow2(Mathf.Max(tileSize.x, 1)), maxTexSize);
			int dataH = Mathf.Min(NextPow2(Mathf.Max(tileSize.y, 1)), maxTexSize);
			int bitmaskTexW = Mathf.Min(dataW * wpt, maxTexSize);
			int bitmaskTexH = Mathf.Min(dataH, maxTexSize);
			int zBinTexW = Mathf.Min(wpt, maxTexSize);
			int zBinTexH = Mathf.Min(s_zBinCount, maxTexSize);
			if (tileBitmaskTexture == null ||
			    tileBitmaskTexture.width < bitmaskTexW ||
			    tileBitmaskTexture.height < bitmaskTexH)
			{
				if (tileBitmaskTexture != null) Object.DestroyImmediate(tileBitmaskTexture);
				tileBitmaskTexture = new Texture2D(bitmaskTexW, bitmaskTexH, rFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}

			if (zBinTexture == null ||
			    zBinTexture.width < zBinTexW ||
			    zBinTexture.height < zBinTexH)
			{
				if (zBinTexture != null) Object.DestroyImmediate(zBinTexture);
				zBinTexture = new Texture2D(zBinTexW, zBinTexH, rFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}

			s_tileDataTexSize = new Vector2Int(tileBitmaskTexture.width / wpt, tileBitmaskTexture.height);
			int actualBitmaskSize = s_tileDataTexSize.x * s_tileDataTexSize.y * wpt;
			if (!tileBitmaskArray.IsCreated || tileBitmaskArray.Length < actualBitmaskSize)
			{
				if (tileBitmaskArray.IsCreated) tileBitmaskArray.Dispose();
				tileBitmaskArray = new NativeArray<uint>(actualBitmaskSize, Allocator.Persistent);
			}
#if !UNITY_WEBGL || UNITY_EDITOR
			if (notOpenGLES)
			{
				if (tileBitmaskBufferSize < actualBitmaskSize)
				{
					tileBitmaskBuffer?.Release();
					tileBitmaskBuffer = new ComputeBuffer(actualBitmaskSize, 4);
					tileBitmaskBufferSize = actualBitmaskSize;
				}

				int zBinTotal = s_zBinCount * wpt;
				if (zBinBufferSize < zBinTotal)
				{
					zBinBuffer?.Release();
					zBinBuffer = new ComputeBuffer(zBinTotal, 4);
					zBinBufferSize = zBinTotal;
				}

				if (lightBoundsBufferSize < maxOtherLightCount)
				{
					lightBoundsBuffer?.Release();
					lightBoundsBuffer = new ComputeBuffer(maxOtherLightCount, 16);
					lightBoundsBufferSize = maxOtherLightCount;
				}

				if (lightZRangesBufferSize < maxOtherLightCount)
				{
					lightZRangesBuffer?.Release();
					lightZRangesBuffer = new ComputeBuffer(maxOtherLightCount, 8); // float2 = 8 bytes
					lightZRangesBufferSize = maxOtherLightCount;
				}
			}
#endif
		}

		void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
		{
			Rect r = visibleLight.screenRect;
			lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
		}
	}
}
