using System.Collections.Generic;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [ExecuteAlways]
    public class LensFlareComponent : MonoBehaviour
    {
        public LensFlareData flareData;
        [Range(0f, 10f)] public float intensity = 1f;
        [Range(0.01f, 10f)] public float scale = 1f;
        public Color colorModulation = Color.white;
        public bool allowOffScreen = false;

        /// <summary>Linked light for color modulation (auto-detected if on same GameObject).</summary>
        [HideInInspector] public Light linkedLight;

        /// <summary>Static list of all active LensFlareComponents, avoids FindObjectsByType per frame.</summary>
        static readonly List<LensFlareComponent> activeComponents = new();
        public static IReadOnlyList<LensFlareComponent> ActiveComponents => activeComponents;

        void OnEnable()
        {
            if (linkedLight == null)
                linkedLight = GetComponent<Light>();
            if (!activeComponents.Contains(this))
                activeComponents.Add(this);
        }

        void OnDisable()
        {
            activeComponents.Remove(this);
        }

        /// <summary>World-space position of the flare source.</summary>
        public Vector3 WorldPosition => transform.position;

        /// <summary>Effective color: colorModulation * (light color if modulateByLightColor).</summary>
        public Color GetColor()
        {
            Color c = colorModulation;
            if (linkedLight != null)
                c *= linkedLight.color * linkedLight.intensity;
            return c;
        }
    }
}
