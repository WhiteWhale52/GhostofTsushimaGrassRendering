Shader "Unlit/Terrain"
{
    Properties
    {
        _MainTex ("Main Tex", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct VertInput
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VertOutput
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldPos : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            VertOutput vert (VertInput v)
            {
                VertOutput o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.worldPos = TransformObjectToWorld(v.vertex).xyz;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            float2 WorldToUV(float3 worldPos, float3 origin, float3 size)
            {
                return (worldPos.xz - origin.xz) / size.xz; // assumes flat plane on XZ
            }

            float4 frag (VertOutput i) : SV_Target
            {
                float2 uv = WorldToUV(i.worldPos, float3(0,0,0), float3(30,0,30));
                uv = (uv * 0.5f) + 0.5f; 
                float4 col = tex2D(_MainTex, uv);
                return col;
            }
            ENDHLSL
        }
    }
}
