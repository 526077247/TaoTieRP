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
        public int lightCount;
        public int2 tileCount;
        public int dataStride;
        public float2 screenUVToTileCoords;
        public int wordsPerTile;

        [NativeDisableParallelForRestriction]
        public NativeArray<uint> tileBitmask;

        public void Execute(int tileIdx)
        {
            int tx = tileIdx % tileCount.x;
            int ty = tileIdx / tileCount.x;

            int linearIdx = ty * dataStride + tx;
            int baseOffset = linearIdx * wordsPerTile;

            for (int i = 0; i < lightCount; i++)
            {
                float4 b = lightBounds[i];
                int minTx = math.max(0, (int)math.ceil(b.x * screenUVToTileCoords.x) - 1);
                int maxTx = math.min(tileCount.x - 1, (int)math.floor(b.z * screenUVToTileCoords.x));
                int minTy = math.max(0, (int)math.ceil(b.y * screenUVToTileCoords.y) - 1);
                int maxTy = math.min(tileCount.y - 1, (int)math.floor(b.w * screenUVToTileCoords.y));

                if (tx >= minTx && tx <= maxTx && ty >= minTy && ty <= maxTy)
                {
                    int wordIdx = i / 32;
                    int bitIdx = i % 32;
                    tileBitmask[baseOffset + wordIdx] |= 1u << bitIdx;
                }
            }
        }
    }
}
