using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Panini Projection")]
    public class PaniniProjectionVolume : VolumeComponent
    {
        public ClampedFloatParameter distance = new(0.5f, 0f, 1f);
        public ClampedFloatParameter cropToFit = new(1f, 0f, 1f);
    }
}
