Shader "Hidden/TaoTie RP/Lens Flare" {
    Properties {
        _FlareTexture ("Texture", 2D) = "white" {}
        _FlareData ("Data", Vector) = (1, 0, 1, 0)
        _FlareColor ("Color", Color) = (1, 1, 1, 1)
        _FlarePolygonSides ("Polygon Sides", Float) = 6
    }
    SubShader {
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha One  // Additive by default

        Pass {
            Name "Lens Flare"

            HLSLPROGRAM
            #include "ShaderLibrary/Common.hlsl"
            #include "ShaderLibrary/LensFlare.hlsl"
            #pragma vertex FlarePassVertex
            #pragma fragment FlareFragment
            #pragma multi_compile _ _FLARE_BLEND_SCREEN _FLARE_BLEND_PREMULTIPLIED _FLARE_BLEND_LERP
            ENDHLSL
        }
    }
}
