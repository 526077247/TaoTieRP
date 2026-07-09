using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Film Grain")]
    public class FilmGrainVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.15f, 0f, 1f);
        public ClampedFloatParameter lumaResponse = new(0.5f, 0f, 1f);
    }
}
