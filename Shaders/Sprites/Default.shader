Shader "TaoTieRP/Sprites/Default"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1, 1, 1, 1)

        [Main(Clipping, _, off, off)]
        _groupClipping ("Clipping", float) = 0
        [Space()]
        [SubToggle(Clipping, _ALPHA_CLIP)] _AlphaClip ("Alpha Clip", Float) = 1
        [Sub(Clipping._ALPHA_CLIP)] _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
        [Space()]
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Z Test (Always = On Top)", Float) = 4
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
    }
    SubShader
    {
        HLSLINCLUDE
        #include "../../ShaderLibrary/Common.hlsl"
        #include "../../ShaderLibrary/Sprites/SpriteInput.hlsl"
        ENDHLSL

        Tags
        {
            "Queue"="AlphaTest"
            "IgnoreProjector"="True"
            "RenderType"="TransparentCutout"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Blend [_SrcBlend] [_DstBlend]

        Pass
        {
            Name "SpritePass"
            Tags { "LightMode"="SRPDefaultUnlit" }

            HLSLPROGRAM
            #pragma shader_feature _ALPHA_CLIP
            #pragma multi_compile_instancing
            #pragma vertex SpritePassVertex
            #pragma fragment SpritePassFragment
            #include "../../ShaderLibrary/Sprites//SpritePass.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "LWGUI.LWGUI"
}
