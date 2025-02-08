using UnityEngine;

namespace TaoTie
{
    [System.Serializable]
    public struct ForwardPlusSettings
    {
        public enum TileSize
        {
            Default, _16 = 16, _32 = 32, _64 = 64, _128 = 128, _256 = 256
        }

        [Tooltip("Tile size in pixels per dimension, default is 64.")]
        public TileSize tileSize;
        
        [Range(0, 99)]
        [Tooltip("Maximum allowed lights per tile, 0 means default, which is 31.")]
        public int maxLightsPerTile;
    }
}