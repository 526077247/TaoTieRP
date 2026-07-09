using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    [VolumeComponentMenu("TaoTie RP/Outline")]
    public class OutlineVolume : VolumeComponent
    {
        public ColorParameter color = new(Color.black, false, true, true);
        public ClampedFloatParameter depthSensitivity = new(0.1f, 0.0001f, 1f);
        public ClampedFloatParameter normalSensitivity = new(0.1f, 0.0001f, 1f);
        public ClampedFloatParameter width = new(1f, 1f, 5f);
    }
}
