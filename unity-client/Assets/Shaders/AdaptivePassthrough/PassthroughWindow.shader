Shader "TeamVR/AdaptivePassthrough/PassthroughWindow"
{
    Properties
    {
        _WorldBottomLeft ("World Bottom Left", Vector) = (-0.5, -0.5, 1, 1)
        _WorldBottomRight ("World Bottom Right", Vector) = (0.5, -0.5, 1, 1)
        _WorldTopRight ("World Top Right", Vector) = (0.5, 0.5, 1, 1)
        _WorldTopLeft ("World Top Left", Vector) = (-0.5, 0.5, 1, 1)
        _Shape ("Shape", Float) = 0
        _Aspect ("Width / Height", Float) = 1
        _Feather ("Edge Feather", Range(0.001, 0.5)) = 0.065
        _RevealStrength ("Reveal Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay+998"
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
            #include "HazardPresentationCommon.hlsl"

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
                float4 _WorldBottomLeft;
                float4 _WorldBottomRight;
                float4 _WorldTopRight;
                float4 _WorldTopLeft;
                float _Shape;
                float _Aspect;
                float _Feather;
                float _RevealStrength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 worldPosition = HazardWorldPosition(
                    input.uv,
                    _WorldBottomLeft.xyz,
                    _WorldBottomRight.xyz,
                    _WorldTopRight.xyz,
                    _WorldTopLeft.xyz);
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float reveal = HazardRevealMask(
                    input.uv,
                    _Shape,
                    _Aspect,
                    _Feather);
                float virtualAlpha =
                    1.0 - saturate(reveal * _RevealStrength);
                return half4(0.0, 0.0, 0.0, virtualAlpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
