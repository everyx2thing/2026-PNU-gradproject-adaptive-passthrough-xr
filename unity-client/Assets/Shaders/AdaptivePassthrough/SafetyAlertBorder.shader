Shader "TeamVR/AdaptivePassthrough/SafetyAlertBorder"
{
    Properties
    {
        _Color ("Border Color", Color) = (1, 0.02, 0.01, 0.95)
        _Thickness ("Border Thickness", Range(0.01, 0.12)) = 0.035
        _Pulse ("Pulse", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay+1100"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "SafetyAlertBorder"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Thickness;
                float _Pulse;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = float4(input.uv * 2.0 - 1.0, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 edgeDistance = min(input.uv, 1.0 - input.uv);
                float nearestEdge = min(edgeDistance.x, edgeDistance.y);
                float feather = max(0.002, _Thickness * 0.20);
                float border = 1.0 - smoothstep(
                    _Thickness,
                    _Thickness + feather,
                    nearestEdge);
                return half4(
                    _Color.rgb,
                    _Color.a * saturate(_Pulse) * border);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
