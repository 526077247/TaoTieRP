using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Sharpen")]
    public class SharpenVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.5f, 0f, 2f);
        public ClampedFloatParameter radius = new(1f, 1f, 5f);
    }
}
