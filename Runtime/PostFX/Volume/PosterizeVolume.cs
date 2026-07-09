using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Posterize")]
    public class PosterizeVolume : VolumeComponent
    {
        public ClampedIntParameter levels = new(8, 2, 256);
    }
}
