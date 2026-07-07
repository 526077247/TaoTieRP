using System.Collections.Generic;
using UnityEngine;

namespace TaoTie.RenderPipelines
{
    /// <summary>
    /// Per-camera motion data for Motion Blur (and TAA).
    /// Tracks previous view-projection matrix for velocity calculation.
    /// </summary>
    public class MotionCameraData
    {
        public Matrix4x4 prevViewProjMatrix;
        public bool hasHistory;

        static readonly Dictionary<Camera, MotionCameraData> dataMap = new();

        public static MotionCameraData Get(Camera camera)
        {
            if (!dataMap.TryGetValue(camera, out var data))
            {
                data = new MotionCameraData();
                dataMap[camera] = data;
            }
            return data;
        }

        public static void CleanupAll()
        {
            dataMap.Clear();
        }
    }
}
