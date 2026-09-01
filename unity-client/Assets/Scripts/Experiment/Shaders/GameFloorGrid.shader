Shader "TeamVR/Experiment/GameFloorGrid"
{
    Properties
    {
        _BaseColor("Base Color", Color) = (0.05, 0.07, 0.10, 1)
        _LineColor("Line Color", Color) = (0.25, 0.55, 0.85, 1)
        _CellSize("Cell Size (m)", Float) = 1.0
        _LineWidth("Line Width", Range(0.001, 0.2)) = 0.02
        _FadeDistance("Fade Distance (m)", Float) = 3.5
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardUnlit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _LineColor;
                float _CellSize;
                float _LineWidth;
                float _FadeDistance;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positions = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positions.positionCS;
                OUT.positionWS = positions.positionWS;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Screen-space-derivative grid: line thickness stays ~1px
                // regardless of distance/grazing angle, which avoids the
                // moire/aliasing a naive frac()+step() grid produces once a
                // cell becomes smaller than a pixel on screen.
                float2 coord = IN.positionWS.xz / max(_CellSize, 0.001);
                float2 lineScale = max(fwidth(coord) * max(_LineWidth * 20.0, 1.0), 1e-5);
                float2 gridAA = abs(frac(coord - 0.5) - 0.5) / lineScale;
                float gridLine = 1.0 - saturate(min(gridAA.x, gridAA.y));

                float distanceToCamera = distance(IN.positionWS, _WorldSpaceCameraPos);
                float fade = saturate(1.0 - distanceToCamera / max(_FadeDistance, 0.001));

                half4 gridColor = lerp(_BaseColor, _LineColor, gridLine);
                half4 color = lerp(_BaseColor, gridColor, fade);
                color.a = 1.0;
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
