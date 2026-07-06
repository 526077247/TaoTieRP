Shader "Hidden/TaoTie RP/Depth Debugger" {
	SubShader {
		Cull Off
		ZTest Always
		ZWrite Off

		HLSLINCLUDE
			#include "../ShaderLibrary/Common.hlsl"
			#include "DepthDebuggerPass.hlsl"
		ENDHLSL

		Pass {
			Name "Depth Visualization"

			Blend SrcAlpha OneMinusSrcAlpha

			HLSLPROGRAM
				#pragma vertex DepthDebuggerVertex
				#pragma fragment DepthDebuggerFragment
			ENDHLSL
		}
	}
}
