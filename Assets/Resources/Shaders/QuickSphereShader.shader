Shader "Unlit/QuickSphereShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normalWS : TEXCOORD1;
            };


            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float NdotL = saturate(dot(i.normalWS, lightDir));
                float3 baseColor = float3(0.5f,0.2f,0.3f);
                float3 finalColor = baseColor * NdotL * _MainLightColor.rgb;
                return float4(finalColor,1);
            }
            ENDHLSL
        }
    }
}