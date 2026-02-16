Shader "Custom/TerrainHeightMap"
{
    Properties
    {
        [Header(Height Map)]
        _HeightMap ("Height Map", 2D) = "white" {}
        _HeightScale ("Height Scale", Float) = 2
        [Header(Base Texture)]
        _MainTex( "Base Texture", 2D) = "white" {} 
    }
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry"
        }

        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct VertInput
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };

            struct VertOutput
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 normal : TEXCOORD1;
            };

            sampler2D _HeightMap;
            SamplerState sampler_HeightMap;

            sampler2D _MainTex;
            
            CBUFFER_START(UnityPerMaterial)
            float _HeightScale;
            CBUFFER_END
            
            VertOutput vert(VertInput v)
            {
                VertOutput o;
                float height = tex2Dlod(_HeightMap, float4(v.uv, 0,0)).g;
                float3 displaced = v.vertex.xyz;
                displaced.y += height * _HeightScale;
                o.vertex = TransformObjectToHClip(displaced);
                o.uv = v.uv;
                o.normal = TransformObjectToWorldNormal(v.normal); 
                return o;
            }

            float4 frag(VertOutput i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);
                return col;
            }
            ENDHLSL
        }
    }
}