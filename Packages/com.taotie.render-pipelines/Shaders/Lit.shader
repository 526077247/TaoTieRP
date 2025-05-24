Shader "TaoTie RP/Lit" {
	
	Properties {
		[Main(Surface, _, off, off)]
		_group ("Surface", float) = 0
		[Space()]
		[Tex(Surface, _BaseColor)]_BaseMap("Texture", 2D) = "white" {}
		[HideInInspector]_BaseColor("Color", Color) = (0.5, 0.5, 0.5, 1.0)
		[Sub(Surface)]_Metallic ("Metallic", Range(0, 1)) = 0
		[Sub(Surface)]_Occlusion ("Occlusion", Range(0, 1)) = 1
		[Sub(Surface)]_Smoothness ("Smoothness", Range(0, 1)) = 0.5
		[SubToggle(Surface, _MASK_MAP)] _MaskMapToggle ("Mask Map", Float) = 0
		[Tex(Surface._MASK_MAP)] _MaskMap("Mask (MODS)", 2D) = "white" {}
		[SubToggle(Surface,_PREMULTIPLY_ALPHA)] _PremulAlpha ("Premultiply Alpha", Float) = 0
		[Sub(Surface)]_Fresnel ("Fresnel", Range(0, 1)) = 1
		[Tex(Surface)] _LightMap("Light Map", 2D) = "bump" {}
		[SubToggle(Surface,_RAMP_MAP)] _RampToggle ("Ramp Map", Float) = 0
		[Tex(Surface._RAMP_MAP)] _RampMap("Ramp", 2D) = "white" {}
		[SubToggle(Surface,_SHADOW_FACE_ON)] _FaceOn ("IsFace", Float) = 0

		
		[Main(Shadows, _, off, off)]
		_groupShadows ("Shadows", float) = 0
		[Space()]
		[SubToggle(Shadows,_RECEIVE_SHADOWS)] _ReceiveShadows ("Receive Shadows", Float) = 1
		[KWEnum(Shadows, On, On, Clip, Clip, Dither, Dither, Off, Off)] _Shadows ("Shadows", Float) = 0

		[Main(Clipping, _, off, off)]
		_groupClipping ("Clipping", float) = 0
		[Space()]
		[SubToggle(Clipping,_CLIPPING)] _Clipping ("Alpha Clipping", Float) = 0
		[Sub(Clipping._CLIPPING)] _Cutoff ("Alpha Cutoff", Range(0.0, 1.0)) = 0.5
		
		[Main(NormalMap, _, off, off)]
		_groupNormalMap ("NormalMap", float) = 0
		[Space()]
		[SubToggle(NormalMap,_NORMAL_MAP)] _NormalMapToggle ("Normal Map", Float) = 0
		[Tex(NormalMap._NORMAL_MAP)] _NormalMap("Normals", 2D) = "bump" {}
		[Sub(NormalMap._NORMAL_MAP)]_NormalScale("Normal Scale", Range(0, 1)) = 1

		[Main(Emission, _, off, off)]
		_groupEmission ("Emission", float) = 0
		[Space()]
		[Tex(Emission,_EmissionColor)] _EmissionMap("Emission", 2D) = "white" {}
		[HideInInspector][HDR] _EmissionColor("Emission", Color) = (0.0, 0.0, 0.0, 0.0)

		[Main(Details, _, off, off)]
		_groupDetails ("Details", float) = 0
		[Space()]
		[SubToggle(Details,_DETAIL_MAP)] _DetailMapToggle ("Detail Maps", Float) = 0
		[Tex(Details._DETAIL_MAP)]_DetailMap("Details", 2D) = "linearGrey" {}
		[Tex(Details._DETAIL_MAP)][NoScaleOffset] _DetailNormalMap("Detail Normals", 2D) = "bump" {}
		[Sub(Details._DETAIL_MAP)]_DetailAlbedo("Detail Albedo", Range(0, 1)) = 1
		[Sub(Details._DETAIL_MAP)]_DetailSmoothness("Detail Smoothness", Range(0, 1)) = 1
		[Sub(Details._DETAIL_MAP)]_DetailNormalScale("Detail Normal Scale", Range(0, 1)) = 1
		
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

		[HideInInspector] _MainTex("Texture for Lightmap", 2D) = "white" {}
		[HideInInspector] _Color("Color for Lightmap", Color) = (0.5, 0.5, 0.5, 1.0)
	}
	
	SubShader {
		HLSLINCLUDE
		#include "ShaderLibrary/Common.hlsl"
		#include "LitInput.hlsl"
		ENDHLSL

		Pass {
			Tags {
				"LightMode" = "CustomLit"
			}

			Blend [_SrcBlend] [_DstBlend], One OneMinusSrcAlpha
			ZWrite [_ZWrite]

			HLSLPROGRAM
			#pragma shader_feature _CLIPPING
			#pragma shader_feature _RECEIVE_SHADOWS
			#pragma shader_feature _PREMULTIPLY_ALPHA
			#pragma shader_feature _MASK_MAP
			#pragma shader_feature _NORMAL_MAP
			#pragma shader_feature _DETAIL_MAP
			#pragma shader_feature _RAMP_MAP
			#pragma shader_feature _SHADOW_FACE_ON
			#pragma multi_compile _ _SHADOW_FILTER_MEDIUM _SHADOW_FILTER_HIGH
			#pragma multi_compile _ _SOFT_CASCADE_BLEND
			#pragma multi_compile _ _SHADOW_MASK_ALWAYS _SHADOW_MASK_DISTANCE
			#pragma multi_compile _ LIGHTMAP_ON
			#pragma multi_compile _ LOD_FADE_CROSSFADE
			#pragma multi_compile_instancing
			#pragma vertex LitPassVertex
			#pragma fragment LitPassFragment
			#include "LitPass.hlsl"
            ENDHLSL
        }
        Pass {
			Tags {
				"LightMode" = "ShadowCaster"
			}

			ColorMask 0

			HLSLPROGRAM
			#pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
			#pragma multi_compile_instancing
			#pragma multi_compile _ LOD_FADE_CROSSFADE
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
            
            #include "ShaderLibrary/NormalOutline.hlsl"
            ENDHLSL
        }
    }
	CustomEditor "LWGUI.LWGUI"
}