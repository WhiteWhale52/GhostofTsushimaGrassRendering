Shader "Unlit/QuickQuadShader"
{
    Properties
    {
        _TiltAmount("Tilt Amount", Float) = 1.0 
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
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };


            CBUFFER_START(UnityPerMaterial)
                float _TiltAmount;
            CBUFFER_END
            
            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                float t = v.uv.y;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float shiftedUVsX = v.uv.x - 0.5f;
                float3 viewDirection = normalize(_WorldSpaceCameraPos - TransformObjectToWorld(v.positionOS));
                float3x3 worldToObject = (float3x3)unity_WorldToObject;
                float localViewDirectionX = mul(worldToObject, viewDirection).x;
                float A = localViewDirectionX * shiftedUVsX;
                float viewDirXAbsPosSat = saturate(pow(abs(localViewDirectionX),1.8f));
                float B = viewDirXAbsPosSat * A;
                float C = _TiltAmount * B;
                float correctedPos = saturate(pow(t + 0.5f, 0.33f) * pow(1 - t, 0.8f));
                float3 normal = TransformObjectToWorldNormal(v.normalOS) * float3(1,1,0) * correctedPos;
                o.vertex += abs(float4(normal,1) * C);
                o.color =  float4(1,1,1,1);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                return i.color;
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