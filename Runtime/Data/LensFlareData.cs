using System;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    [Serializable]
    public struct LensFlareElement
    {
        public enum ElementType { Image, Circle, Polygon }
        public enum BlendMode { Additive, Screen, Premultiplied, Lerp }

        public ElementType type;
        public Texture2D imageTexture;
        [Range(3, 8)] public int polygonSides;
        public Color tint;
        public bool modulateByLightColor;
        [Range(0f, 10f)] public float intensity;
        public BlendMode blendMode;
        [Range(-1f, 1f)] public float position;
        [Range(0.01f, 5f)] public float sizeScale;
        [Range(0f, 360f)] public float rotation;
        [Range(0f, 360f)] public float angularOffset;
        [Range(0f, 1f)] public float translationScale;
        public bool enableOcclusion;
        [Range(0.01f, 1f)] public float occlusionRadius;
        [Range(1, 32)] public int occlusionSampleCount;
        [Range(0f, 1f)] public float occlusionBias;
    }

    [CreateAssetMenu(menuName = "Rendering/TaoTie Lens Flare Data")]
    public class LensFlareData : ScriptableObject
    {
        public LensFlareElement[] elements = new LensFlareElement[]
        {
            new LensFlareElement
            {
                type = LensFlareElement.ElementType.Circle,
                tint = Color.white,
                intensity = 1f,
                blendMode = LensFlareElement.BlendMode.Additive,
                position = 0f,
                sizeScale = 0.3f,
                translationScale = 0f,
                polygonSides = 6,
                enableOcclusion = true,
                occlusionRadius = 0.1f,
                occlusionSampleCount = 8,
                occlusionBias = 0.1f,
            },
            new LensFlareElement
            {
                type = LensFlareElement.ElementType.Circle,
                tint = new Color(1f, 0.8f, 0.5f, 1f),
                intensity = 0.5f,
                blendMode = LensFlareElement.BlendMode.Additive,
                position = 0.5f,
                sizeScale = 0.15f,
                translationScale = 0.5f,
                polygonSides = 6,
                enableOcclusion = true,
                occlusionRadius = 0.05f,
                occlusionSampleCount = 4,
                occlusionBias = 0.1f,
            },
        };
    }
}
