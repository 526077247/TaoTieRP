Shader "TaoTie RP/Unlit"
{
    Properties
    {
		[Main(Base, _, off, off)]
		_group ("Base", float) = 0
		[Space()]
		[Tex(Base, _BaseColor)]_BaseMap("Texture", 2D) = "white" {}
		[HideInInspector][HDR]_BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)

		[Main(Clipping, _, off, off)]
		_groupClipping ("Clipping", float) = 0
		[Space()]
		[SubToggle(Clipping,_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0
		[Sub(Clipping._CLIPPING)] _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
    	
    	[Main(Outline, _, off, off)]
        _groupOutline ("OutlineSettings", float) = 1
		[Space()]
		[SubToggle(Outline, _OUTLINE)] _Outline("Use Outline", Float) = 0.0
		[Sub(Outline._OUTLINE)] _OutlineColor ("Outline Color", Color) = (0,0,0,0)
        [Sub(Outline._OUTLINE)] _OutlineWidth ("Outline Width", Range(0, 10)) = 1
    	[Space(50)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
		[Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("Z Write", Float) = 1
    }
    SubShader
    {
        HLSLINCLUDE
		#include "../ShaderLibrary/Common.hlsl"
		#include "UnlitInput.hlsl"
		ENDHLSL
        
        Tags { "RenderType"="Opaque" }
        LOD 100
       
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            HLSLPROGRAM
            #pragma shader_feature _CLIPPING
            #pragma multi_compile_instancing
            #pragma vertex UnlitPassVertex
			#pragma fragment UnlitPassFragment
			#include "UnlitPass.hlsl"
            ENDHLSL
        }
    	Pass
		{
			Tags
			{
				"LightMode" = "ShadowCaster"
			}

			ColorMask 0

			HLSLPROGRAM
			#pragma target 3.5
			#pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
			#pragma multi_compile_instancing
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			#include "ShadowCasterPass.hlsl"
			ENDHLSL
		}
        Pass {
			Tags {
				"LightMode" = "Meta"
			}

			Cull Off

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex MetaPassVertex
			#pragma fragment MetaPassFragment
			#include "MetaPass.hlsl"
			ENDHLSL
		}
    	Pass
        {
            Name "OutLine"
            Tags {"LightMode" = "Outline" }
            Cull Front
            ZWrite[_ZWrite]
            BlendOp Add, Max
            ZTest LEqual
            Offset 1, 1

            HLSLPROGRAM
            #pragma multi_compile _ _OUTLINE
            #pragma vertex NormalOutLineVertex
            #pragma fragment NormalOutlineFragment
            
            #include "../ShaderLibrary/NormalOutline.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "LWGUI.LWGUI"
}
