Shader "TeamVR/AdaptivePassthrough/HazardCue"
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
        _Pulse ("Pulse Progress", Range(0, 1)) = 0
        _CueMode ("Cue Mode", Float) = 0
        _RevealStrength ("Reveal Strength", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Overlay+999"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

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
                float _Pulse;
                float _CueMode;
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
                float3 center = 0.25 * (
                    _WorldBottomLeft.xyz
                    + _WorldBottomRight.xyz
                    + _WorldTopRight.xyz
                    + _WorldTopLeft.xyz);
                worldPosition = center
                    + (worldPosition - center) * (1.0 + _Pulse * 0.13);
                output.positionCS = TransformWorldToHClip(worldPosition);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float inside = HazardInsideDistance(
                    input.uv,
                    _Shape,
                    _Aspect);
                // The ring centre expands with the vertex geometry above;
                // its own thickness remains 2.5% of the short axis.
                float outlineWidth = 0.025;
                float ring = 1.0 - smoothstep(
                    outlineWidth,
                    outlineWidth + max(_Feather * 0.35, 0.002),
                    abs(inside));
                float centerLine = 1.0 - smoothstep(
                    0.008,
                    0.018,
                    abs(input.uv.x - 0.5));
                float corridorFill = smoothstep(0.0, _Feather, inside) * 0.18;
                float corridor = _CueMode > 0.5
                    ? max(corridorFill, max(ring * 0.75, centerLine * 0.85))
                    : ring * (1.0 - _Pulse);
                float alpha = corridor * saturate(_RevealStrength) * 0.9;
                return half4(1.0, 0.02, 0.01, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
