Shader "TeamVR/AdaptivePassthrough/PassthroughWindow"
{
    Properties
    {
        _Rect ("Viewport Rect", Vector) = (0.25, 0.25, 0.5, 0.5)
        _Feather ("Edge Feather", Range(0.001, 0.5)) = 0.12
        _RevealStrength ("Reveal Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay+1000"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "PassthroughWindow"
            ZWrite Off
            ZTest Always
            Cull Off
            BlendOp Add
            Blend Zero SrcAlpha
            ColorMask RGBA

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
                float4 _Rect;
                float _Feather;
                float _RevealStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 viewportPosition =
                    _Rect.xy + input.uv * _Rect.zw;
                output.positionCS = float4(
                    viewportPosition * 2.0 - 1.0,
                    0.0,
                    1.0);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 edgeDistance =
                    min(input.uv, 1.0 - input.uv);
                float nearestEdge =
                    min(edgeDistance.x, edgeDistance.y);
                float reveal =
                    smoothstep(0.0, max(_Feather, 0.001), nearestEdge);
                float virtualAlpha =
                    1.0 - saturate(reveal * _RevealStrength);
                return half4(0.0, 0.0, 0.0, virtualAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
