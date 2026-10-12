// Sunset gradient for the title scene, painted on the inside of a huge sphere. Fog does not touch it.
Shader "CompanyGame/TitleSky"
{
    Properties
    {
        _Zenith("Zenith", Color) = (0.125, 0.15, 0.35, 1)
        _Mid("Mid", Color) = (0.29, 0.25, 0.53, 1)
        _Pink("Pink", Color) = (0.65, 0.34, 0.54, 1)
        _Horizon("Horizon", Color) = (0.95, 0.63, 0.49, 1)
        _Below("Below", Color) = (0.83, 0.52, 0.49, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Cull Front
        ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Zenith, _Mid, _Pink, _Horizon, _Below;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dir = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float h = normalize(input.dir).y;
                half3 c = lerp(_Below.rgb, _Horizon.rgb, smoothstep(-0.12, 0.0, h));
                c = lerp(c, _Pink.rgb, smoothstep(0.0, 0.18, h));
                c = lerp(c, _Mid.rgb, smoothstep(0.12, 0.45, h));
                c = lerp(c, _Zenith.rgb, smoothstep(0.4, 1.0, h));
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
