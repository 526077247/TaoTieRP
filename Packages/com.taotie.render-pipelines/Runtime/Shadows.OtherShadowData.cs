using System.Runtime.InteropServices;
using UnityEngine;

namespace TaoTie
{
    partial class Shadows
    {
        [StructLayout(LayoutKind.Sequential)]
        struct OtherShadowData
        {
            public const int stride = 4 * 4 + 4 * 16;

            public Vector4 tileData;

            public Matrix4x4 shadowMatrix;

            public OtherShadowData(
                Vector2 offset,
                float scale,
                float bias,
                float border,
                Matrix4x4 matrix)
            {
                tileData.x = offset.x * scale + border;
                tileData.y = offset.y * scale + border;
                tileData.z = scale - border - border;
                tileData.w = bias;
                shadowMatrix = matrix;
            }
        }
    }
}