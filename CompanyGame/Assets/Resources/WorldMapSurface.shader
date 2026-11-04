Shader "CompanyGame/WorldMapSurface"
{
    Properties { _ZTest ("Depth test", Float) = 7 }
    SubShader
    {
        Pass
        {
            Cull Back ZWrite On ZTest [_ZTest]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; };
            struct v2f { float4 pos:SV_POSITION; float3 normal:TEXCOORD0; };
            float4 _MapColor;
            v2f vert(appdata v) { v2f o; o.pos=UnityObjectToClipPos(v.vertex); o.normal=UnityObjectToWorldNormal(v.normal); return o; }
            fixed4 frag(v2f i):SV_Target
            {
                float3 c=_MapColor.rgb;
                float grey=dot(c,float3(.299,.587,.114));
                c=lerp(c,grey.xxx,.16);
                c=lerp(c,float3(.91,.86,.75),.12);
                float light=.72+.28*saturate(dot(normalize(i.normal),normalize(float3(-.4,1,.3))));
                return float4(c*light,1);
            }
            ENDCG
        }
    }
}
