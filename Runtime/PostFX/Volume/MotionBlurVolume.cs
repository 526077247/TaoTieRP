using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Motion Blur")]
    public class MotionBlurVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.3f, 0f, 1f);
        public ClampedIntParameter sampleCount = new(16, 4, 32);
    }
}
