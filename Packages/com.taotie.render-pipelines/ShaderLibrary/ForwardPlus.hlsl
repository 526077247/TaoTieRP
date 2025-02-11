#ifndef TAOTIE_FORWARD_PLUS_INCLUDED
#define TAOTIE_FORWARD_PLUS_INCLUDED

// xy: Screen UV to tile coordinates.
// z: Tiles per row, as integer.
// w: Tile data size, as integer.
float4 _ForwardPlusTileSettings;

#define MAX_TILES_COUNT 1280
CBUFFER_START(_ForwardPlus)
    int _ForwardPlusTileLength;
    int4 _ForwardPlusTileLights[MAX_TILES_COUNT];
    int _ForwardPlusTiles[MAX_TILES_COUNT+1];
CBUFFER_END


struct ForwardPlusTile
{
    int2 coordinates;

    int index;

    int GetForwardPlusTiles(int temp)
    {
        int offset = floor(temp * 0.25 + 0.1);
        int type = round(temp - offset * 4);
        return _ForwardPlusTileLights[offset][type];
    }
	
    int GetTileDataSize()
    {
        return asint(_ForwardPlusTileSettings.w);
    }

    int GetHeaderIndex()
    {
        return _ForwardPlusTiles[index];
    }

    int GetLightCount()
    {
        return _ForwardPlusTiles[index+1] - _ForwardPlusTiles[index];
    }

    int GetFirstLightIndexInTile()
    {
        return GetHeaderIndex();
    }

    int GetLastLightIndexInTile()
    {
        return GetHeaderIndex() + GetLightCount() - 1;
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
    tile.index = tile.coordinates.y * asint(_ForwardPlusTileSettings.z) +
        tile.coordinates.x;
    return tile;
}

#endif