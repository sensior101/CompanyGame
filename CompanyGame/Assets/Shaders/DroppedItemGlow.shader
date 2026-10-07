Shader "CompanyGame/DroppedItemGlow"
{
    Properties
    {
        [HDR] _Tint ("Golden light", Color) = (1, .73, .12, 1)
        _Intensity ("Intensity", Float) = 1.7
        _Beam ("Vertical beam", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half _Intensity;
                half _Beam;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half radial = pow(saturate(1.0h - length(input.uv * 2.0h - 1.0h)), 1.8h);
                half beam = pow(saturate(1.0h - abs(input.uv.x * 2.0h - 1.0h)), 1.6h)
                    * pow(saturate(1.0h - input.uv.y), 1.5h) * smoothstep(0.0h, .04h, input.uv.y);
                half alpha = lerp(radial, beam, _Beam) * _Tint.a * input.color.a;
                return half4(_Tint.rgb * input.color.rgb * _Intensity, alpha);
            }
            ENDHLSL
        }
    }
}
