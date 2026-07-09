using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Chromatic Aberration")]
    public class ChromaticAberrationVolume : VolumeComponent
    {
        public ClampedFloatParameter intensity = new(0.1f, 0f, 1f);
        public Vector2Parameter center = new(new Vector2(0.5f, 0.5f));
    }
}
