using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Lens Distortion")]
    public class LensDistortionVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.3f, -1f, 1f);
        public Vector2Parameter center = new(new Vector2(0.5f, 0.5f));
        public ClampedFloatParameter scale = new(1f, 0.1f, 2f);
    }
}
