using System.Collections.Generic;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    /// <summary>
    /// One screen-space circular mask supplied by the game side.
    /// Circle: xy = center in viewport UV, z = inner radius, w = outer radius
    /// (both in viewport-height units; x is corrected by aspect in the shader).
    /// </summary>
    public struct ScreenMaskCircle
    {
        public Vector4 Circle;
        public float Strength;
        /// <summary>Player anchor depth in linear eye space; only fragments in front of it are clipped.</summary>
        public float EyeDepth;

        public ScreenMaskCircle(Vector4 circle, float strength, float eyeDepth)
        {
            Circle = circle;
            Strength = strength;
            EyeDepth = eyeDepth;
        }
    }

    /// <summary>
    /// Game-side data source for screen-space masks. The render pipeline owns
    /// the mechanism only; it never decides what a mask means.
    /// </summary>
    public interface IScreenMaskProvider
    {
        int GetCircleCount(Camera camera);

        bool TryGetCircle(Camera camera, int index, out ScreenMaskCircle circle);
    }

    /// <summary>
    /// Per-frame data handed to the render passes. Arrays are owned by the
    /// caller and reused to avoid allocations in the render loop.
    /// </summary>
    public struct ScreenMaskData
    {
        public Vector4[] Circles;
        public float[] Strengths;
        public float[] EyeDepths;
        public int Count;
        public float Aspect;

        public bool Enabled
        {
            get { return Count > 0 && Circles != null && Strengths != null && EyeDepths != null; }
        }
    }

    /// <summary>
    /// Registry of mask providers. Register once at startup (or on enable)
    /// and unregister on teardown.
    /// </summary>
    public static class ScreenMaskProviderRegistry
    {
        public const int MaxCircles = 4;

        static readonly List<IScreenMaskProvider> providers = new List<IScreenMaskProvider>();

        public static int ProviderCount
        {
            get { return providers.Count; }
        }

        public static void Register(IScreenMaskProvider provider)
        {
            if (provider == null || providers.Contains(provider))
                return;
            providers.Add(provider);
        }

        public static void Unregister(IScreenMaskProvider provider)
        {
            if (provider != null)
                providers.Remove(provider);
        }

        /// <summary>
        /// Fills the caller-owned arrays and returns the number of valid circles.
        /// </summary>
        public static int Collect(Camera camera, Vector4[] circles, float[] strengths,
            float[] eyeDepths)
        {
            if (circles == null || strengths == null || eyeDepths == null)
                return 0;

            int total = 0;
            for (int p = 0; p < providers.Count && total < MaxCircles; p++)
            {
                IScreenMaskProvider provider = providers[p];
                if (provider == null)
                    continue;

                int count = provider.GetCircleCount(camera);
                for (int i = 0; i < count && total < MaxCircles; i++)
                {
                    ScreenMaskCircle circle;
                    if (!provider.TryGetCircle(camera, i, out circle))
                        continue;

                    circles[total] = circle.Circle;
                    strengths[total] = Mathf.Clamp01(circle.Strength);
                    eyeDepths[total] = circle.EyeDepth;
                    total++;
                }
            }
            return total;
        }
    }
}
