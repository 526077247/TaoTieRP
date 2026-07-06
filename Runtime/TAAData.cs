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

        float jitterScale = 1.0f;

        public Vector2 GetJitter()
        {
            // URP-style Halton sequence: skip index 0 for shadow stability, wrap at 1024
            int index = (frameIndex & 1023) + 1;
            return new Vector2(
                (Halton(index, 2) - 0.5f) * jitterScale,
                (Halton(index, 3) - 0.5f) * jitterScale
            );
        }

        public void SetJitterScale(float scale)
        {
            jitterScale = Mathf.Clamp01(scale);
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
