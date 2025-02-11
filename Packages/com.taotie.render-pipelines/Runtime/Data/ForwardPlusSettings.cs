using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [System.Serializable]
    public struct ForwardPlusSettings
    {
        public enum TileSize
        {
            Default, Small = 32, Normal = 64, Mid = 128, Large = 256
        }

        [Tooltip("Tile size in pixels per dimension, default is Normal.")]
        public TileSize tileSize;
        
        [Range(0, 128)]
        [Tooltip("Maximum allowed lights per tile, 0 means default, which is 32.")]
        public int maxLightsPerTile;
    }
}