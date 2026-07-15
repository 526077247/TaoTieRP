using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace TaoTie.RenderPipelines
{
    [BurstCompile]
    struct TileCullJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float4> lightBounds;
        public int2 tileCount;
        public int dataStride;
        public float2 screenUVToTileCoords;
        public int wordsPerTile;

        [NativeDisableParallelForRestriction]
        public NativeArray<uint> tileBitmask;

        public void Execute(int lightIdx)
        {
            float4 b = lightBounds[lightIdx];

            int minTx = math.max(0, (int)math.ceil(b.x * screenUVToTileCoords.x) - 1);
            int maxTx = math.min(tileCount.x - 1, (int)math.floor(b.z * screenUVToTileCoords.x));
            int minTy = math.max(0, (int)math.ceil(b.y * screenUVToTileCoords.y) - 1);
            int maxTy = math.min(tileCount.y - 1, (int)math.floor(b.w * screenUVToTileCoords.y));

            int wordIdx = lightIdx / 32;
            int bitIdx = lightIdx % 32;
            uint bitMask = 1u << bitIdx;

            for (int ty = minTy; ty <= maxTy; ty++)
            {
                for (int tx = minTx; tx <= maxTx; tx++)
                {
                    int linearIdx = ty * dataStride + tx;
                    int baseOffset = linearIdx * wordsPerTile;
                    tileBitmask[baseOffset + wordIdx] |= bitMask;
                }
            }
        }
    }
}
