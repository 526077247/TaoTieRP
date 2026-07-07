#ifndef TAOTIE_FORWARD_PLUS_INCLUDED
#define TAOTIE_FORWARD_PLUS_INCLUDED

// xy: Screen UV to tile coordinates.
// z: Tiles per row, as integer.
// w: Tile data size (maxLightsPerTile), as integer.
float4 _ForwardPlusTileSettings;

// Use separate property names to avoid property sheet type conflicts
// between Texture2D and StructuredBuffer shader variants.
#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
    StructuredBuffer<float2> _ForwardPlusTilesBuf;
    StructuredBuffer<float> _ForwardPlusTileLightsBuf;
#else
    TEXTURE2D(_ForwardPlusTilesTex);
    TEXTURE2D(_ForwardPlusTileLightsTex);
#endif

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
        #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
            return (int)_ForwardPlusTileLightsBuf[temp];
        #else
            return (int)_ForwardPlusTileLightsTex.Load(int3(FpLightTexCoord(temp), 0)).r;
        #endif
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
    #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
        // _ForwardPlusLightTexSize.z = tileDataTexSize.x (data stride for linear indexing)
        int dataStride = (int)_ForwardPlusLightTexSize.z;
        int linearIndex = tile.coordinates.y * dataStride + tile.coordinates.x;
        float2 data = _ForwardPlusTilesBuf[linearIndex];
        tile.headerIndex = (int)data.x;
        tile.lightCount = (int)data.y;
    #else
        float4 data = _ForwardPlusTilesTex.Load(int3(tile.coordinates, 0));
        tile.headerIndex = (int)data.r;
        tile.lightCount = (int)data.g;
    #endif
    tile.index = tile.coordinates.y * (int)_ForwardPlusTileSettings.z +
        tile.coordinates.x;
    return tile;
}

#endif
