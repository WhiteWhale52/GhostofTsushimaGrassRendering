Shader "Unlit/SimpleShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        
        Pass
        {
            Tags
            {
                "LightMode" = "UniversalForward"
            }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #pragma multi_compile DOTS_INSTANCING_ON

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"


            struct Metadata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };


             CBUFFER_START(UnityPerMaterial)
             float4x4 _ObjectToWorld;
             ByteAddressBuffer _InstanceData;
             CBUFFER_END
            
            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
              // UNITY_DOTS_INSTANCED_PROP(float4x4, _ObjectToWorld)
            UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
          // #define _ObjectToWorld UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4x4, _ObjectToWorld)
            float4 LoadFloat4(uint byteOffset)
            {
                return asfloat(_InstanceData.Load4(byteOffset));
            }

            float4x3 LoadMatrix(uint byteOffset)
            {
                return float4x3(
                    LoadFloat4(byteOffset + 0),
                    LoadFloat4(byteOffset + 16),
                    LoadFloat4(byteOffset + 32)
                );
            }
            
            v2f vert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID, Metadata v)
            {
                v2f o;

                uint byteOffset = instanceID * 128;
                
                float4x3 objectToWorld = LoadMatrix(byteOffset + 0);
                 float4x3 worldToObject = LoadMatrix(byteOffset + 64);

                 float3 localPos = v.vertex;
                 float4 worldPos = mul(objectToWorld, float4(localPos,1));
                 o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                // Reconstruct world position (float3)
                // float3 worldPos = float3(
                //     dot(m[0], localPos),
                //     dot(m[1], localPos),
                //     dot(m[2], localPos)
                // );
           //     float3 worldPos = TransformObjectToWorld(localPos);
                
                // Convert to clip space
              //  o.vertex = mul(UNITY_MATRIX_VP, worldPos);
                o.uv = v.uv;
                return o;
            }

            v2f vert(Metadata IN)
             {
                 v2f OUT;

                 UNITY_SETUP_INSTANCE_ID(IN);
                 UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                 const VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.vertex.xyz);
                 OUT.vertex = positionInputs.positionCS;
                 return OUT;
             }
            
            float4 frag (v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                return float4(1,0,0,1);
            }
            ENDHLSL
        }
    }
}
