Shader "TaoTie RP/Particles/Unlit"
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
        
    	[Main(NearFade, _, off, off)]
		_groupNearFade ("NearFade", float) = 0
		[Space()]
        [SubToggle(NearFade,_NEAR_FADE)] _NearFade ("Near Fade", Float) = 0
		[Sub(NearFade._NEAR_FADE)]_NearFadeDistance ("Near Fade Distance", Range(0.0, 10.0)) = 1
		[Sub(NearFade._NEAR_FADE)]_NearFadeRange ("Near Fade Range", Range(0.01, 10.0)) = 1
    	
    	[Main(SoftParticles, _, off, off)]
		_groupSoftParticles ("SoftParticles", float) = 0
		[Space()]
	    [SubToggle(SoftParticles,_SOFT_PARTICLES)] _SoftParticles ("Soft Particles", Float) = 0
		[Sub(SoftParticles._SOFT_PARTICLES)]_SoftParticlesDistance ("Soft Particles Distance", Range(0.0, 10.0)) = 0
		[Sub(SoftParticles._SOFT_PARTICLES)]_SoftParticlesRange ("Soft Particles Range", Range(0.01, 10.0)) = 1
    	
    	[Main(Distortion, _, off, off)]
		_groupDistortion ("Distortion", float) = 0
		[Space()]
	    [SubToggle(Distortion,_DISTORTION)] _Distortion ("Distortion", Float) = 0
		[Tex(Distortion._DISTORTION)] _DistortionMap("Distortion Vectors", 2D) = "bumb" {}
		[Sub(Distortion._DISTORTION)]_DistortionStrength("Distortion Strength", Range(0.0, 0.2)) = 0.1
    	[Sub(Distortion._DISTORTION)]_DistortionBlend("Distortion Blend", Range(0.0, 1.0)) = 1
    	
	    [Toggle(_VERTEX_COLORS)] _VertexColors ("Vertex Colors", Float) = 0
        [Toggle(_FLIPBOOK_BLENDING)] _FlipbookBlending ("Flipbook Blending", Float) = 0
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
            #pragma shader_feature _VERTEX_COLORS
            #pragma shader_feature _FLIPBOOK_BLENDING
            #pragma shader_feature _NEAR_FADE
            #pragma shader_feature _SOFT_PARTICLES
            #pragma shader_feature _DISTORTION
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
			#pragma shader_feature _VERTEX_COLORS
			#pragma shader_feature _FLIPBOOK_BLENDING
			#pragma shader_feature _ _SHADOWS_CLIP _SHADOWS_DITHER
			#pragma multi_compile_instancing
			#pragma vertex ShadowCasterPassVertex
			#pragma fragment ShadowCasterPassFragment
			#include "ShadowCasterPass.hlsl"
			ENDHLSL
		}
    }
    CustomEditor "LWGUI.LWGUI"
}
