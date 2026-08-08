using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Tilt Shift")]
    public class TiltShiftVolume : VolumeComponent
    {
        public ClampedFloatParameter focusOffset = new(0f, -1f, 1f);
        public ClampedFloatParameter focusRange = new(0.15f, 0.01f, 0.5f);
        public ClampedFloatParameter smoothness = new(0.2f, 0.01f, 1f);
        public ClampedFloatParameter blurStrength = new(1f, 0f, 3f);
        public ClampedIntParameter blurDirection = new(1, 0, 2);
    }
}
