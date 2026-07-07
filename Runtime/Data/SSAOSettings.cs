using System;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public class SSAOSettings
    {
        public enum SampleCount { Low = 4, Medium = 8, High = 12 }

        [Tooltip("Enable Screen Space Ambient Occlusion.")]
        public bool enabled = false;

        [Tooltip("Sample count per pixel. Higher = better quality but slower.")]
        public SampleCount sampleCount = SampleCount.Medium;

        [Range(0.1f, 5f)]
        [Tooltip("World-space radius of the AO sampling sphere.")]
        public float radius = 0.5f;

        [Range(0f, 4f)]
        [Tooltip("Intensity of the darkening effect.")]
        public float intensity = 1f;

        [Range(0.5f, 10f)]
        [Tooltip("Distance at which AO fades out. Lower = AO only near camera.")]
        public float falloff = 3f;

        [Range(0.25f, 1f)]
        [Tooltip("Resolution scale. 1 = full resolution, 0.5 = half resolution.")]
        public float downsample = 0.5f;
    }
}
