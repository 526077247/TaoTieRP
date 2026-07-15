#ifndef TAOTIE_FORWARD_PLUS_INCLUDED
#define TAOTIE_FORWARD_PLUS_INCLUDED

// xy: Screen UV to tile coordinates.
// z: Tiles per row, as integer.
// w: wordsPerTile (number of uint32 words per tile bitmask)
float4 _ForwardPlusTileSettings;

// x: dataStride (texture width / wordsPerTile, power-of-2)
// y: zBinCount
// z: wordsPerTile
// w: unused
float4 _ForwardPlusDataSize;

// x: zBinCount
// y: camera near
// z: 1 / (far - near)
// w: camera far
float4 _ZBinParams;

#if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
    StructuredBuffer<uint> _ForwardPlusTileBitmaskBuf;
    StructuredBuffer<uint> _ForwardPlusZBinBuf;
#else
    TEXTURE2D(_ForwardPlusTileBitmaskTex);
    TEXTURE2D(_ForwardPlusZBinTex);
#endif

int GetWordsPerTile()
{
    return (int)_ForwardPlusTileSettings.w;
}

int GetZBinCount()
{
    return (int)_ZBinParams.x;
}

// Compute ZBin index from linear eye depth (positive, distance from camera)
int GetZBinIndex(float eyeDepth)
{
    float normalizedDepth = saturate((eyeDepth - _ZBinParams.y) * _ZBinParams.z);
    return clamp((int)(normalizedDepth * _ZBinParams.x), 0, (int)_ZBinParams.x - 1);
}

// Read a single uint32 word from the tile bitmask
uint LoadTileBitmaskWord(int2 tileCoord, int wordIdx)
{
    int dataStride = (int)_ForwardPlusDataSize.x;
    int linearIdx = tileCoord.y * dataStride + tileCoord.x;
    int bufferIdx = linearIdx * GetWordsPerTile() + wordIdx;
    #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
        return _ForwardPlusTileBitmaskBuf[bufferIdx];
    #else
        int texW = (int)_ForwardPlusDataSize.x * GetWordsPerTile();
        int x = bufferIdx % texW;
        int y = bufferIdx / texW;
        return asuint(_ForwardPlusTileBitmaskTex.Load(int3(x, y, 0)).r);
    #endif
}

// Read a single uint32 word from the ZBin bitmask
uint LoadZBinBitmaskWord(int zBinIdx, int wordIdx)
{
    int bufferIdx = zBinIdx * GetWordsPerTile() + wordIdx;
    #if !defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3) && !defined(SHADER_API_GLCORE)
        return _ForwardPlusZBinBuf[bufferIdx];
    #else
        int texW = GetWordsPerTile();
        int x = bufferIdx % texW;
        int y = bufferIdx / texW;
        return asuint(_ForwardPlusZBinTex.Load(int3(x, y, 0)).r);
    #endif
}

struct ForwardPlusTile
{
    int2 coordinates;
    int index;
    int wordsPerTile;

    int GetWordsPerTile()
    {
        return wordsPerTile;
    }

    uint GetBitmaskWord(int wordIdx)
    {
        return LoadTileBitmaskWord(coordinates, wordIdx);
    }

    int GetLightCount()
    {
        int count = 0;
        for (int w = 0; w < wordsPerTile; w++)
            count += countbits(GetBitmaskWord(w));
        return count;
    }

    int GetMaxLights()
    {
        return wordsPerTile * 32;
    }

    bool IsMinimumEdgePixel(float2 screenUV)
    {
        float2 startUV = coordinates / _ForwardPlusTileSettings.xy;
        return any(screenUV - startUV < _CameraBufferSize.xy);
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
    tile.wordsPerTile = GetWordsPerTile();
    tile.index = tile.coordinates.y * (int)_ForwardPlusTileSettings.z +
        tile.coordinates.x;
    return tile;
}

#endif
