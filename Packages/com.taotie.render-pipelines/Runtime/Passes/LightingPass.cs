using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering;
using Unity.Mathematics;

namespace TaoTie.RenderPipelines
{
	public class LightingPass
	{
		static readonly ProfilingSampler sampler = new("Lighting");

		private const int maxDirLightCount = 4, maxOtherLightCount = 128, maxTileCount = 1280;

		static readonly int
			dirLightCountId = Shader.PropertyToID("_DirectionalLightCount"),
			dirLightColorsId = Shader.PropertyToID("_DirectionalLightColors"),
			dirLightDirectionsAndMasksId = Shader.PropertyToID("_DirectionalLightDirectionsAndMasks"),
			dirLightShadowDataId = Shader.PropertyToID("_DirectionalLightShadowData"),
			tilesId = Shader.PropertyToID("_ForwardPlusTiles"),
			tilesLightId = Shader.PropertyToID("_ForwardPlusTileLights"),
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
		
		Vector4[] lightBounds = new Vector4[maxOtherLightCount];
		private float[] tileDataArray = new float[maxTileCount+1];
		private Vector4[] tileLightArray = new Vector4[maxTileCount];
		
		private int maxLightsPerTile;
		
		public void Setup(
			CullingResults cullingResults, Vector2Int attachmentSize, 
			ForwardPlusSettings forwardPlusSettings, ShadowSettings shadowSettings,
			int renderingLayerMask)
		{
			this.cullingResults = cullingResults;
			shadows.Setup(cullingResults, shadowSettings);
			
			maxLightsPerTile = forwardPlusSettings.maxLightsPerTile <= 0 ?
				32 : forwardPlusSettings.maxLightsPerTile;

			
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
			int i;
			dirLightCount = otherLightCount = 0;
			for (i = 0; i < visibleLights.Length; i++)
			{
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
								SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupPointLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
						case LightType.Spot:
							if (otherLightCount < maxOtherLightCount)
							{
								SetupForwardPlus(otherLightCount, ref visibleLight);
								SetupSpotLight(
									otherLightCount++, i, ref visibleLight, light);
							}

							break;
					}
				}
			}

			var tileScreenUVSize = math.float2(
				1f / screenUVToTileCoordinates.x,
				1f / screenUVToTileCoordinates.y);
			var tileCount = TileCount;
			if (tileCount > maxTileCount)
			{
				tileCount = maxTileCount;
				tileDataArray = new float[TileCount + 1];
				tileLightArray = new Vector4[TileCount];
				Debug.LogError("请增加tileSize tileCount = " + TileCount);
			}

			for (int j = 0; j < tileCount; j++)
			{
				ExecuteTile(j, this.tileCount.x, tileScreenUVSize);
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
			

			buffer.SetGlobalVectorArray(tilesLightId, tileLightArray);
			buffer.SetGlobalFloatArray(tilesId, tileDataArray);
			buffer.SetGlobalVector(tileSettingsId, new Vector4(
				screenUVToTileCoordinates.x, screenUVToTileCoordinates.y,
				tileCount.x.ReinterpretAsFloat(),
				maxLightsPerTile.ReinterpretAsFloat()));
			
			context.renderContext.ExecuteCommandBuffer(buffer);
			buffer.Clear();
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
		
		void ExecuteTile(int tileIndex,int tilesPerRow, float2 tileScreenUVSize)
		{
			int y = tileIndex / tilesPerRow;
			int x = tileIndex - y * tilesPerRow;
			var bounds = math.float4(x, y, x + 1, y + 1) * tileScreenUVSize.xyxy;

			int headerIndex = (int)tileDataArray[tileIndex];

			int dataIndex = headerIndex;
			int lightsInTileCount = 0;

			for (int i = 0; i < otherLightCount; i++)
			{
				float4 b = lightBounds[i];
				if (math.all(math.float4(b.xy, bounds.xy) <= math.float4(bounds.zw, b.zw)))
				{
					if (dataIndex / 4 > tileLightArray.Length)
					{
						Debug.LogError("请增加tileSize");
						break;
					}
					tileLightArray[dataIndex/4][dataIndex%4] = i;
					if (lightsInTileCount + 1 >= maxLightsPerTile)
					{
						break;
					}

					lightsInTileCount++;
					dataIndex++;
				}
			}
			
			tileDataArray[tileIndex + 1] = headerIndex + lightsInTileCount;
		}
	}
}