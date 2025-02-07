using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;

namespace TaoTie
{
    partial class LightingPass
    {
        [StructLayout(LayoutKind.Sequential)]
        struct OtherLightData
        {
            public const int stride = 4 * 4 * 5;

            public Vector4 color, position, directionAndMask, spotAngle, shadowData;

            public static OtherLightData CreatePointLight(
                ref VisibleLight visibleLight, Light light, Vector4 shadowData)
            {
                OtherLightData data;
                data.color = visibleLight.finalColor;
                data.position = visibleLight.localToWorldMatrix.GetColumn(3);
                data.position.w = 1f / Mathf.Max(
                    visibleLight.range * visibleLight.range, 0.00001f);
                data.spotAngle = new Vector4(0f, 1f);
                data.directionAndMask = Vector4.zero;
                data.directionAndMask.w =
                    light.renderingLayerMask.ReinterpretAsFloat();
                data.shadowData = shadowData;
                return data;
            }

            public static OtherLightData CreateSpotLight(
                ref VisibleLight visibleLight, Light light, Vector4 shadowData)
            {
                OtherLightData data;
                data.color = visibleLight.finalColor;
                data.position = visibleLight.localToWorldMatrix.GetColumn(3);
                data.position.w = 1f / Mathf.Max(
                    visibleLight.range * visibleLight.range, 0.00001f);
                //data.spotAngle = new Vector4(0f, 1f);
                data.directionAndMask =
                    -visibleLight.localToWorldMatrix.GetColumn(2);
                data.directionAndMask.w =
                    light.renderingLayerMask.ReinterpretAsFloat();

                float innerCos = Mathf.Cos(
                    Mathf.Deg2Rad * 0.5f * light.innerSpotAngle);
                float outerCos = Mathf.Cos(
                    Mathf.Deg2Rad * 0.5f * visibleLight.spotAngle);
                float angleRangeInv = 1f / Mathf.Max(innerCos - outerCos, 0.001f);
                data.spotAngle = new Vector4(
                    angleRangeInv, -outerCos * angleRangeInv
                );
                data.shadowData = shadowData;
                return data;
            }
        }
    }
}