Shader "Hidden/TaoTie RP/Camera Renderer" {
	
	SubShader {
		Cull Off
		ZTest Always
		ZWrite Off
		
		HLSLINCLUDE
		#include "ShaderLibrary/Common.hlsl"
		#include "CameraRendererPasses.hlsl"
		ENDHLSL

		Pass {
			Name "Copy"

			Blend [_CameraSrcBlend] [_CameraDstBlend]
			
			HLSLPROGRAM
				#pragma vertex DefaultPassVertex
				#pragma fragment CopyPassFragment
			ENDHLSL
		}
		Pass {
			Name "Copy Depth"

			ColorMask 0
			ZWrite On
			
			HLSLPROGRAM
				#pragma vertex DefaultPassVertex
				#pragma fragment CopyDepthPassFragment
			ENDHLSL
		}
	}
}