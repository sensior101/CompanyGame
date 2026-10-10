Shader "CompanyGame/VehicleHoverOutline"
{
    Properties { _OutlineWidth("Outline width", Float) = 0.008 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+1" }
        Pass
        {
            Name "VehicleOutline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _OutlineWidth;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                positionWS+=TransformObjectToWorldNormal(input.normalOS)*_OutlineWidth;
                output.positionCS=TransformWorldToHClip(positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
    }
}
