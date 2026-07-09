using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Vignette")]
    public class VignetteVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.8f, 0f, 3f);
        public ClampedFloatParameter smoothness = new(0.5f, 0.01f, 1f);
        public Vector2Parameter center = new(new Vector2(0.5f, 0.5f));
        public ClampedFloatParameter roundness = new(1f, 0.1f, 2f);
        public ColorParameter color = new(new Color(0f, 0f, 0f, 1f), false, true, true);
    }
}
