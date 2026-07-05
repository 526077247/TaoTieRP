Shader "Hidden/TaoTie RP/Camera Debugger"
{	
	SubShader
	{
		Cull Off
		ZTest Always
		ZWrite Off
		
		HLSLINCLUDE
		#include "ShaderLibrary/Common.hlsl"
		#include "CameraDebuggerPasses.hlsl"
		ENDHLSL

		Pass
		{
			Name "Forward+ Tiles"

			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
				#pragma target 4.5
				#pragma multi_compile _ _TAOTIE_FORWARD_PLUS
				#pragma multi_compile _ _COMPUTE_BUFFER
				#pragma vertex DefaultPassVertex
				#pragma fragment ForwardPlusTilesPassFragment
			ENDHLSL
		}
	}
}