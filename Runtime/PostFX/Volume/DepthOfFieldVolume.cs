using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Depth Of Field")]
    public class DepthOfFieldVolume : VolumeComponent
    {
        public ClampedFloatParameter focusDistance = new(10f, 0.1f, 100f);
        public ClampedFloatParameter focusRange = new(3f, 0.1f, 50f);
        public ClampedFloatParameter blurStrength = new(1f, 0f, 2f);
    }
}
