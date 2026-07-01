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

		private const int maxDirLightCount = 4, maxOtherLightCount = 256, maxForwardOtherLightCount = 8;

		static readonly int
			dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
			dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
			dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
			dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
			tilesId = Shader.PropertyToID("_ForwardPlusTilesTex"),
			tilesLightId = Shader.PropertyToID("_ForwardPlusTileLightsTex"),
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
			forwardOtherLightColors = new Vector4[maxForwardOtherLightCount],
			forwardOtherLightPositions = new Vector4[maxForwardOtherLightCount],
			forwardOtherLightDirectionsAndMasks = new Vector4[maxForwardOtherLightCount],
			forwardOtherLightSpotAngles = new Vector4[maxForwardOtherLightCount],
			forwardOtherLightShadowData = new Vector4[maxForwardOtherLightCount];

		CullingResults cullingResults;

		readonly Shadows shadows = new();

		int dirLightCount, otherLightCount;

		Vector2 screenUVToTileCoordinates;
		Vector2Int tileCount;
		Vector2Int tileDataTexSize;
		int TileCount => tileCount.x * tileCount.y;

		Vector4[] lightBounds = new Vector4[maxOtherLightCount];
		private Vector2[] tileDataArray = new Vector2[1];
		private float[] tileLightArray = new float[1];
		private static Texture2D tileDataTexture;
		private static Texture2D tileLightTexture;

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
				max = maxForwardOtherLightCount;
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

				var tileScreenUVSize = math.float2(
					1f / screenUVToTileCoordinates.x,
					1f / screenUVToTileCoordinates.y);
				int runningOffset = 0;
				for (int j = 0; j < tileCountTotal; j++)
				{
					ExecuteTile(j, this.tileCount.x, tileScreenUVSize, ref runningOffset);
				}
			}
		}

		void Render(RenderGraphContext context)
		{
			CommandBuffer buffer = context.cmd;

			buffer.SetGlobalInt(dirLightCountId, dirLightCount);
			if (dirLightCount > 0)
			{
				buffer.SetGlobalVectorArray(dirLightColorsId, dirLightColors);
				buffer.SetGlobalVectorArray(
					dirLightDirectionsAndMasksId, dirLightDirectionsAndMasks);
				buffer.SetGlobalVectorArray(
					dirLightShadowDataId, dirLightShadowData);
			}

			buffer.SetGlobalInt(otherLightCountId, otherLightCount);
			if (otherLightCount > 0)
			{
				if (useForwardPlus)
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
					Array.Copy(otherLightColors, forwardOtherLightColors, maxForwardOtherLightCount);
					Array.Copy(otherLightPositions, forwardOtherLightPositions, maxForwardOtherLightCount);
					Array.Copy(otherLightDirectionsAndMasks, forwardOtherLightDirectionsAndMasks, maxForwardOtherLightCount);
					Array.Copy(otherLightSpotAngles, forwardOtherLightSpotAngles, maxForwardOtherLightCount);
					Array.Copy(otherLightShadowData, forwardOtherLightShadowData, maxForwardOtherLightCount);
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

			shadows.Render(context);

			if (useForwardPlus && tileDataTexture != null && tileLightTexture != null)
			{
				tileDataTexture.SetPixelData(tileDataArray, 0);
				tileDataTexture.Apply();
				tileLightTexture.SetPixelData(tileLightArray, 0);
				tileLightTexture.Apply();
				buffer.SetGlobalTexture(tilesId, tileDataTexture);
				buffer.SetGlobalTexture(tilesLightId, tileLightTexture);
				buffer.SetGlobalVector(tileSettingsId, new Vector4(
					screenUVToTileCoordinates.x, screenUVToTileCoordinates.y,
					tileCount.x,
					maxLightsPerTile));
				buffer.SetGlobalVector(lightTexSizeId, new Vector4(
					tileLightTexture.width, tileLightTexture.height, 0, 0));
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
			dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
			dirLightDirectionsAndMasks[index] = dirAndMask;
			dirLightShadowData[index] =
				shadows.ReserveDirectionalShadows(light, visibleIndex);
		}

		void SetupPointLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			otherLightColors[index] = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w =
				1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			otherLightPositions[index] = position;
			otherLightSpotAngles[index] = new Vector4(0f, 1f);
			Vector4 dirAndmask = Vector4.zero;
			dirAndmask.w = light.renderingLayerMask.ReinterpretAsFloat();
			otherLightDirectionsAndMasks[index] = dirAndmask;
			otherLightShadowData[index] = shadows.ReserveOtherShadows(
				light, visibleIndex);
		}

		void SetupSpotLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			otherLightColors[index] = GetFinalColor(ref visibleLight);
			Vector4 position = visibleLight.localToWorldMatrix.GetColumn(3);
			position.w =
				1f / Mathf.Max(visibleLight.range * visibleLight.range, 0.00001f);
			otherLightPositions[index] = position;
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
			otherLightDirectionsAndMasks[index] = dirAndMask;

			float innerCos = Mathf.Cos(Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
			float outerCos = Mathf.Cos(
				Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
			float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
			otherLightSpotAngles[index] = new Vector4(
				angleRangeInv, -outerCos * angleRangeInv
			);
			otherLightShadowData[index] = shadows.ReserveOtherShadows(
				light, visibleIndex);
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
			tileDataTexSize = new Vector2Int(dataW, dataH);
			int dataTexSize = dataW * dataH;

			lightCount = Mathf.Max(lightCount, 1);
			int lightTexW = Mathf.Min(NextPow2(Mathf.CeilToInt(Mathf.Sqrt(lightCount))), maxTexSize);
			lightTexW = Mathf.Max(lightTexW, 1);
			int lightTexH = Mathf.Min(NextPow2(Mathf.CeilToInt(lightCount / (float)lightTexW)), maxTexSize);
			lightTexH = Mathf.Max(lightTexH, 1);
			int lightTexSize = lightTexW * lightTexH;

			var rgbaFormat = GetSupportedRGFormat();
			var rFormat = GetSupportedSingleChannelFormat();

			if (tileDataTexture == null || tileDataTexture.width != dataW || tileDataTexture.height != dataH || tileDataTexture.format != rgbaFormat)
			{
				if (tileDataTexture != null) Object.DestroyImmediate(tileDataTexture);
				tileDataTexture = new Texture2D(dataW, dataH, rgbaFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}
			if (tileLightTexture == null || tileLightTexture.width != lightTexW || tileLightTexture.height != lightTexH || tileLightTexture.format != rFormat)
			{
				if (tileLightTexture != null) Object.DestroyImmediate(tileLightTexture);
				tileLightTexture = new Texture2D(lightTexW, lightTexH, rFormat, false)
				{
					filterMode = FilterMode.Point,
					wrapMode = TextureWrapMode.Clamp
				};
			}
			if (tileDataArray.Length != dataTexSize)
			{
				tileDataArray = new Vector2[dataTexSize];
			}
			if (tileLightArray.Length != lightTexSize)
			{
				tileLightArray = new float[lightTexSize];
			}
		}

		void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
		{
			Rect r = visibleLight.screenRect;
			lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
		}

		void ExecuteTile(int tileIndex, int tilesPerRow, float2 tileScreenUVSize, ref int runningOffset)
		{
			int y = tileIndex / tilesPerRow;
			int x = tileIndex - y * tilesPerRow;
			var bounds = math.float4(x, y, x + 1, y + 1) * tileScreenUVSize.xyxy;

			int headerIndex = runningOffset;
			int dataIndex = headerIndex;
			int lightsInTileCount = 0;

			for (int i = 0; i < otherLightCount; i++)
			{
				float4 b = lightBounds[i];
				if (math.all(math.float4(b.xy, bounds.xy) <= math.float4(bounds.zw, b.zw)))
				{
					if (dataIndex >= tileLightArray.Length)
					{
						Debug.LogError("请增加tileSize");
						break;
					}
					tileLightArray[dataIndex] = i;
					if (lightsInTileCount + 1 > maxLightsPerTile)
					{
						break;
					}

					lightsInTileCount++;
					dataIndex++;
				}
			}

			int texIndex = y * tileDataTexSize.x + x;
			tileDataArray[texIndex] = new Vector2(headerIndex, lightsInTileCount);
			runningOffset += lightsInTileCount;
		}
	}
}
