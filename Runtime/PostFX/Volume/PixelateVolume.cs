using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Pixelate")]
    public class PixelateVolume : VolumeComponent
    {
        public ClampedIntParameter cellSize = new(8, 2, 64);
    }
}
