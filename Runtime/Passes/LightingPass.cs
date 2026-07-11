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
			_ => 256,
		};

		static readonly int wordsPerTile = Mathf.CeilToInt(maxOtherLightCount / 32f);

		private static readonly bool notOpenGLES = SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES2 &&
		                                           SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLES3 &&
		                                           SystemInfo.graphicsDeviceType != GraphicsDeviceType.OpenGLCore;

		static readonly int
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
			zBinParamsId = Shader.PropertyToID("_ZBinParams");

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

		static readonly Vector2[] lightZRanges = new Vector2[maxOtherLightCount];

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
		static float[] s_candidateScores;
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
			s_otherLightCount = 0;
			int max;
			if (s_useForwardPlus)
			{
				max = s_maxLightsPerTile * TileCount;
				if (maxOtherLightCount < max)
					max = maxOtherLightCount;
			}
			else if (s_useDeferred)
			{
				max = maxOtherLightCount;
			}
			else
			{
				max = shadowSettings.maxOtherLights;
				if (max > maxOtherLightCount)
					max = maxOtherLightCount;
			}

			// First pass: process Directional lights + collect Other light candidates
			// Use static arrays to avoid per-frame allocation
			if (s_candidateIndices == null || s_candidateIndices.Length < visibleLights.Length)
			{
				s_candidateIndices = new int[visibleLights.Length];
				s_candidateScores = new float[visibleLights.Length];
			}

			int candidateCount = 0;
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
							s_candidateIndices[candidateCount] = i;
							Color c = visibleLight.finalColor;
							float brightness = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
							Rect r = visibleLight.screenRect;
							float screenArea = r.width * r.height;
							Vector4 lightPos4 = visibleLight.localToWorldMatrix.GetColumn(3);
							Vector3 lightPos = new(lightPos4.x, lightPos4.y, lightPos4.z);
							float distSqr = Vector3.SqrMagnitude(lightPos - cameraPosition);
							s_candidateScores[candidateCount] = brightness * screenArea / Mathf.Max(distSqr, 1f);
							candidateCount++;
							break;
					}
				}
			}

			// Partial selection: keep top `max` candidates by score when exceeding limit
			int otherLimit = Mathf.Min(candidateCount, max);
			if (candidateCount > max)
			{
				// Quick partial selection: only ensure the top `max` entries are the highest scores.
				for (int a = 0; a < max; a++)
				{
					int best = a;
					for (int b = a + 1; b < candidateCount; b++)
					{
						if (s_candidateScores[b] > s_candidateScores[best])
							best = b;
					}

					if (best != a)
					{
						(s_candidateScores[a], s_candidateScores[best]) =
							(s_candidateScores[best], s_candidateScores[a]);
						(s_candidateIndices[a], s_candidateIndices[best]) =
							(s_candidateIndices[best], s_candidateIndices[a]);
					}
				}
			}

			// Second pass: setup Other lights by priority order
			for (i = 0; i < otherLimit; i++)
			{
				int vi = s_candidateIndices[i];
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
			if (s_useForwardPlus)
			{
				int tileCountTotal = TileCount;
				EnsureTileBuffers(s_tileCount);
				EnsureComputeKernel();
				EnsureLightBoundsNative();
				EnsureLightZRangesNative();
				ComputeLightZRanges();
				BuildZBinData();
				bool useGpuCompute = SystemInfo.supportsComputeShaders && CullComputeShader != null && cullKernel >= 0;
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
				lightZRanges[i] = new Vector2(
					Mathf.Max(centerZ - range, s_cameraNear),
					Mathf.Min(centerZ + range, s_cameraFar));
				lightZRangesNative[i] = lightZRanges[i];
			}
		}

		static Matrix4x4 s_viewMatrix;

		void BuildZBinData()
		{
			int zBinTotal = s_zBinCount * wordsPerTile;
			if (!zBinData.IsCreated || zBinData.Length < zBinTotal)
			{
				if (zBinData.IsCreated) zBinData.Dispose();
				zBinData = new NativeArray<uint>(zBinTotal, Allocator.Persistent);
			}

			for (int i = 0; i < zBinTotal; i++)
				zBinData[i] = 0;
			float invDepthRange = 1f / Mathf.Max(s_cameraFar - s_cameraNear, 0.0001f);
			for (int i = 0; i < s_otherLightCount; i++)
			{
				int binMin = Mathf.Clamp(
					Mathf.FloorToInt((lightZRanges[i].x - s_cameraNear) * invDepthRange * s_zBinCount),
					0, s_zBinCount - 1);
				int binMax = Mathf.Clamp(
					Mathf.CeilToInt((lightZRanges[i].y - s_cameraNear) * invDepthRange * s_zBinCount),
					0, s_zBinCount - 1);
				int wordIdx = i / 32;
				uint bitMask = 1u << (i % 32);
				for (int b = binMin; b <= binMax; b++)
					zBinData[b * wordsPerTile + wordIdx] |= bitMask;
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
#if !UNITY_WEBGL || UNITY_EDITOR
			bool useGpuCompute = canUseBuffer && CullComputeShader != null && cullKernel >= 0;
#else
			bool useGpuCompute = false;
#endif
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
						s_tileCount.x, s_tileCount.y, s_tileDataTexSize.x, wordsPerTile));
					CullComputeShader.SetVector("_ScreenUVToTileCoords", s_screenUVToTileCoords);
					CullComputeShader.SetVector("_ScreenSize", new Vector2(s_screenSize.x, s_screenSize.y));
					// Set depth texture for 2.5D culling (only when DepthPrePass is available)
					CullComputeShader.SetInt("_UseDepth25D", s_useDepth25D ? 1 : 0);
					if (s_useDepth25D)
					{
						Texture depthTex = Shader.GetGlobalTexture(Shader.PropertyToID("_CameraDepthTexture"));
						if (depthTex != null)
							CullComputeShader.SetTexture(cullKernel, "_DepthTexture", depthTex);
						float n = s_cameraNear;
						float f = s_cameraFar;
						float fn = f * n;
						float range = f - n;
						Vector4 zBufferParams = SystemInfo.usesReversedZBuffer
							? new Vector4(-1 + f / n, 1, range / fn, 1 / f)
							: new Vector4(1 - f / n, f / n, -range / fn, 1 / n);
						CullComputeShader.SetVector("_DepthZBufferParams", zBufferParams);
					}

					int groupX = Mathf.CeilToInt(s_tileCount.x / 8f);
					int groupY = Mathf.CeilToInt(s_tileCount.y / 8f);
					buffer.DispatchCompute(CullComputeShader, cullKernel, groupX, groupY, 1);
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
				s_tileDataTexSize.x, s_zBinCount, wordsPerTile, 0));
			buffer.SetGlobalVector(zBinParamsId, new Vector4(
				s_zBinCount, s_cameraNear,
				1f / Mathf.Max(s_cameraFar - s_cameraNear, 0.0001f),
				s_cameraFar));
			buffer.SetGlobalVector(tileSettingsId, new Vector4(
				s_screenUVToTileCoords.x, s_screenUVToTileCoords.y,
				s_tileCount.x,
				wordsPerTile));
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
			dirAndMask.w = (float) light.renderingLayerMask;
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
			dirAndmask.w = (float) light.renderingLayerMask;
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
			if (tileBitmaskArray.IsCreated) tileBitmaskArray.Dispose();
			if (zBinData.IsCreated) zBinData.Dispose();
			if (lightBoundsNative.IsCreated) lightBoundsNative.Dispose();
			if (lightZRangesNative.IsCreated) lightZRangesNative.Dispose();
			if (tileBitmaskTexture != null) Object.DestroyImmediate(tileBitmaskTexture);
			if (zBinTexture != null) Object.DestroyImmediate(zBinTexture);
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

		void EnsureLightBoundsNative()
		{
			if (!lightBoundsNative.IsCreated)
				lightBoundsNative = new NativeArray<float4>(maxOtherLightCount, Allocator.Persistent);
		}

		void EnsureLightZRangesNative()
		{
			if (!lightZRangesNative.IsCreated)
				lightZRangesNative = new NativeArray<float2>(maxOtherLightCount, Allocator.Persistent);
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
				wordsPerTile = wordsPerTile,
				tileBitmask = tileBitmaskArray,
			};
			JobHandle handle = job.Schedule(tileCountTotal, 64);
			handle.Complete();
		}

		void EnsureTileBuffers(Vector2Int tileSize)
		{
			int dataW = Mathf.Min(NextPow2(Mathf.Max(tileSize.x, 1)), maxTexSize);
			int dataH = Mathf.Min(NextPow2(Mathf.Max(tileSize.y, 1)), maxTexSize);
			int bitmaskTexW = Mathf.Min(dataW * wordsPerTile, maxTexSize);
			int bitmaskTexH = Mathf.Min(dataH, maxTexSize);
			int zBinTexW = Mathf.Min(wordsPerTile, maxTexSize);
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

			s_tileDataTexSize = new Vector2Int(tileBitmaskTexture.width / wordsPerTile, tileBitmaskTexture.height);
			int actualBitmaskSize = s_tileDataTexSize.x * s_tileDataTexSize.y * wordsPerTile;
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

				int zBinTotal = s_zBinCount * wordsPerTile;
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
