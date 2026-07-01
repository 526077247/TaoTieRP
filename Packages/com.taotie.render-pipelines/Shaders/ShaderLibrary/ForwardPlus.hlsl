#ifndef TAOTIE_FORWARD_PLUS_INCLUDED
#define TAOTIE_FORWARD_PLUS_INCLUDED

// xy: Screen UV to tile coordinates.
// z: Tiles per row, as integer.
// w: Tile data size (maxLightsPerTile), as integer.
float4 _ForwardPlusTileSettings;

TEXTURE2D(_ForwardPlusTileLightsTex);
TEXTURE2D(_ForwardPlusTilesTex);

float4 _ForwardPlusLightTexSize;

int2 FpLightTexCoord(int index)
{
    return int2(index % (int)_ForwardPlusLightTexSize.x, index / (int)_ForwardPlusLightTexSize.x);
}

struct ForwardPlusTile
{
    int2 coordinates;

    int index;
    int headerIndex;
    int lightCount;

    int GetForwardPlusTiles(int temp)
    {
        return (int)_ForwardPlusTileLightsTex.Load(int3(FpLightTexCoord(temp), 0)).r;
    }

    int GetTileDataSize()
    {
        return (int)_ForwardPlusTileSettings.w;
    }

    int GetFirstLightIndexInTile()
    {
        return headerIndex;
    }

    int GetLastLightIndexInTile()
    {
        return headerIndex + lightCount - 1;
    }

    int GetLightIndex(int lightIndexInTile)
    {
        return GetForwardPlusTiles(lightIndexInTile);
    }

    bool IsMinimumEdgePixel(float2 screenUV)
    {
        float2 startUV = coordinates / _ForwardPlusTileSettings.xy;
        return any(screenUV - startUV < _CameraBufferSize.xy);
    }

    int GetMaxLightsPerTile()
    {
        return GetTileDataSize();
    }

    int2 GetScreenSize()
    {
        return int2(round(_CameraBufferSize.zw / _ForwardPlusTileSettings.xy));
    }
};

ForwardPlusTile GetForwardPlusTile(float2 screenUV)
{
    ForwardPlusTile tile;
    tile.coordinates = int2(screenUV * _ForwardPlusTileSettings.xy);
    float4 data = _ForwardPlusTilesTex.Load(int3(tile.coordinates, 0));
    tile.headerIndex = (int)data.r;
    tile.lightCount = (int)data.g;
    tile.index = tile.coordinates.y * (int)_ForwardPlusTileSettings.z +
        tile.coordinates.x;
    return tile;
}

#endif
