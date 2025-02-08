using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Unity.Jobs;
using Unity.Mathematics;

namespace TaoTie
{

	public class LightingPass
	{
		static readonly ProfilingSampler sampler = new("Lighting");

		private const int maxDirLightCount = 4, maxOtherLightCount = 128;

		static readonly int
			dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
			dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
			dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
			dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
			tilesId = Shader.PropertyToID("_ForwardPlusTiles"),
			tileSettingsId = Shader.PropertyToID("_ForwardPlusTileSettings");

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

		CullingResults cullingResults;

		readonly Shadows shadows = new();

		int dirLightCount, otherLightCount;

		Vector2 screenUVToTileCoordinates;
		Vector2Int tileCount;
		int TileCount => tileCount.x * tileCount.y;
		
		NativeArray<float4> lightBounds;
		NativeArray<int> tileData;
		JobHandle forwardPlusJobHandle;

		private Vector4[] tileDataArray;
		private int tileDataArrayLength = -1;
		int maxLightsPerTile, tileDataSize;
		
		public void Setup(
			CullingResults cullingResults, Vector2Int attachmentSize, 
			ForwardPlusSettings forwardPlusSettings, ShadowSettings shadowSettings,
			int renderingLayerMask)
		{
			this.cullingResults = cullingResults;
			shadows.Setup(cullingResults, shadowSettings);
			
			maxLightsPerTile = forwardPlusSettings.maxLightsPerTile <= 0 ?
				31 : forwardPlusSettings.maxLightsPerTile;
			tileDataSize = maxLightsPerTile + 1;
			lightBounds = new NativeArray<float4>(
				maxOtherLightCount, Allocator.TempJob,
				NativeArrayOptions.UninitializedMemory);
			float tileScreenPixelSize = forwardPlusSettings.tileSize <= 0 ?
				64f : (float)forwardPlusSettings.tileSize;
			tileScreenPixelSize *= Mathf.Pow(2,
				Mathf.FloorToInt(Mathf.Sqrt((attachmentSize.x / 1366f) * (attachmentSize.y / 768f))-0.1f));
			screenUVToTileCoordinates.x = attachmentSize.x / tileScreenPixelSize;
			screenUVToTileCoordinates.y = attachmentSize.y / tileScreenPixelSize;
			tileCount.x = Mathf.CeilToInt(screenUVToTileCoordinates.x);
			tileCount.y = Mathf.CeilToInt(screenUVToTileCoordinates.y);

			SetupLights(renderingLayerMask);
		}

		void SetupLights(int renderingLayerMask)
		{
			NativeArray<VisibleLight> visibleLights = cullingResults.visibleLights;
			int requiredMaxLightsPerTile = Mathf.Min(
				maxLightsPerTile, visibleLights.Length);
			tileDataSize = requiredMaxLightsPerTile + 1;
			int i;
			dirLightCount = otherLightCount = 0;
			for (i = 0; i < visibleLights.Length; i++)
			{
				int newIndex = -1;
				VisibleLight visibleLight = visibleLights[i];
				Light light = visibleLight.light;
				if ((light.renderingLayerMask & renderingLayerMask) != 0)
				{
					switch (visibleLight.lightType)
					{
						case LightType.Directional:
							if (dirLightCount < maxDirLightCount)
							{
								SetupDirectionalLight(
									dirLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Point:
							if (otherLightCount < maxOtherLightCount)
							{
								newIndex = otherLightCount;
								SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupPointLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Spot:
							if (otherLightCount < maxOtherLightCount)
							{
								newIndex = otherLightCount;
								SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupSpotLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
					}
				}
			}
			
			tileData = new NativeArray<int>(
				TileCount * tileDataSize, Allocator.TempJob);
			forwardPlusJobHandle = new ForwardPlusTilesJob
			{
				lightBounds = lightBounds,
				tileData = tileData,
				otherLightCount = otherLightCount,
				tileScreenUVSize = math.float2(
					1f / screenUVToTileCoordinates.x,
					1f / screenUVToTileCoordinates.y),
				maxLightsPerTile = requiredMaxLightsPerTile,
				tilesPerRow = tileCount.x,
				tileDataSize = tileDataSize
			}.ScheduleParallel(TileCount, tileCount.x, default);
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

			shadows.Render(context);

			forwardPlusJobHandle.Complete();
			if (tileData.Length/4 + 1 != tileDataArrayLength)
			{
				tileDataArrayLength = tileData.Length/4 + 1;
				tileDataArray = new Vector4[tileDataArrayLength];
			}
			for (int i = 0; i < tileData.Length; i++)
			{
				tileDataArray[i/4][i%4] = tileData[i];
			}

			buffer.SetGlobalVectorArray(tilesId, tileDataArray);
			buffer.SetGlobalVector(tileSettingsId, new Vector4(
				screenUVToTileCoordinates.x, screenUVToTileCoordinates.y,
				tileCount.x.ReinterpretAsFloat(),
				tileDataSize.ReinterpretAsFloat()));
			
			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
			lightBounds.Dispose();
			tileData.Dispose();
		}

		void SetupDirectionalLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			dirLightColors[index] = visibleLight.finalColor;
			Vector4 dirAndMask = -visibleLight.localToWorldMatrix.GetColumn(2);
			dirAndMask.w = light.renderingLayerMask.ReinterpretAsFloat();
			dirLightDirectionsAndMasks[index] = dirAndMask;
			dirLightShadowData[index] =
				shadows.ReserveDirectionalShadows(light, visibleIndex);
		}

		void SetupPointLight(
			int index, int visibleIndex, ref VisibleLight visibleLight, Light light)
		{
			otherLightColors[index] = visibleLight.finalColor;
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
			otherLightColors[index] = visibleLight.finalColor;
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
			CullingResults cullingResults,ForwardPlusSettings forwardPlusSettings,Vector2Int attachmentSize, ShadowSettings shadowSettings,
			int renderingLayerMask)
		{
			using RenderGraphBuilder builder = renderGraph.AddRenderPass(
				sampler.name, out LightingPass pass, sampler);
			pass.Setup(cullingResults, attachmentSize,forwardPlusSettings, shadowSettings,
				 renderingLayerMask);
			builder.SetRenderFunc<LightingPass>(
				static (pass, context) => pass.Render(context));
			builder.AllowPassCulling(false);
			return pass.shadows.GetRenderTextures(renderGraph, builder);
		}
		
		void SetupForwardPlus(int lightIndex, ref VisibleLight visibleLight)
		{
			Rect r = visibleLight.screenRect;
			lightBounds[lightIndex] = math.float4(r.xMin, r.yMin, r.xMax, r.yMax);
		}
	}
}