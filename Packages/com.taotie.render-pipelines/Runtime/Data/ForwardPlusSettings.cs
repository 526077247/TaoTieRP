using UnityEngine;

namespace TaoTie
{
    [System.Serializable]
    public struct ForwardPlusSettings
    {
        public enum TileSize
        {
            Default, Small = 64, Mid = 128, Large = 256
        }

        [Tooltip("Tile size in pixels per dimension, default is Small.")]
        public TileSize tileSize;
        
        [Range(0, 31)]
        [Tooltip("Maximum allowed lights per tile, 0 means default, which is 31.")]
        public int maxLightsPerTile;
    }
}