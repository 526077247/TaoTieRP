using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace TaoTie.RenderPipelines
{
    public class TAACameraData
    {
        public RTHandle historyRT;
        public Matrix4x4 prevViewProjMatrix;
        public int frameIndex;
        public bool hasHistory;

        static readonly Dictionary<Camera, TAACameraData> dataMap = new();

        public static TAACameraData Get(Camera camera)
        {
            if (!dataMap.TryGetValue(camera, out var data))
            {
                data = new TAACameraData();
                dataMap[camera] = data;
            }
            return data;
        }

        public static void Cleanup(Camera camera)
        {
            if (dataMap.TryGetValue(camera, out var data))
            {
                if (data.historyRT != null)
                {
                    data.historyRT.Release();
                    RTHandles.Release(data.historyRT);
                    data.historyRT = null;
                }
                dataMap.Remove(camera);
            }
        }

        public static void CleanupAll()
        {
            foreach (var kvp in dataMap)
            {
                if (kvp.Value.historyRT != null)
                {
                    kvp.Value.historyRT.Release();
                    RTHandles.Release(kvp.Value.historyRT);
                }
            }
            dataMap.Clear();
        }

        public void EnsureHistoryTexture(int width, int height, bool useHDR)
        {
            if (historyRT != null &&
                (historyRT.rt.width != width || historyRT.rt.height != height))
            {
                historyRT.Release();
                RTHandles.Release(historyRT);
                historyRT = null;
            }

            if (historyRT == null)
            {
                var format = useHDR &&
                    SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, FormatUsage.Render)
                    ? GraphicsFormat.R16G16B16A16_SFloat
                    : GraphicsFormat.R8G8B8A8_UNorm;

                historyRT = RTHandles.Alloc(
                    width, height, 1, DepthBits.None, format,
                    FilterMode.Bilinear, TextureWrapMode.Clamp,
                    name: "TAA History");
                hasHistory = false;
            }
        }

        float jitterScale = 0.5f;
        float jitterSpread = 1f;

        public Vector2 GetJitter()
        {
            return GetHaltonJitter(frameIndex % 16, jitterScale, jitterSpread);
        }

        public void SetJitterParams(float scale, float spread)
        {
            jitterScale = scale;
            jitterSpread = spread;
        }

        public static Vector2 GetHaltonJitter(int index, float scale = 0.5f, float spread = 1f)
        {
            return new Vector2(
                (Halton(index + 1, 2) - 0.5f) * spread * scale,
                (Halton(index + 1, 3) - 0.5f) * spread * scale
            );
        }

        static float Halton(int index, int radix)
        {
            float result = 0f;
            float f = 1f;
            while (index > 0)
            {
                f /= radix;
                result += f * (index % radix);
                index /= radix;
            }
            return result;
        }

        public void AdvanceFrame()
        {
            frameIndex++;
            hasHistory = true;
        }
    }
}
