Shader "Custom/TestingGroundsShader"
{
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" "Geometry" = "Opaque" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct VertexInput
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct VertexOutput
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
            };


            VertexOutput vert (VertexInput v)
            {
                VertexOutput o;
                float t = v.uv.x;
                const VertexPositionInputs positionInputs = GetVertexPositionInputs(v.positionOS);
                const VertexNormalInputs normalInputs = GetVertexNormalInputs(v.normalOS);
                float3 normalWS = normalInputs.normalWS;

             //   o.positionCS = GetVertexPositionInputs(v.positionOS).positionCS;
                
                 float newUVsY = v.uv.y - 0.5f;
                 float3 TransformedCameraDir = _WorldSpaceCameraPos - positionInputs.positionCS;
                 float A = pow(abs(TransformedCameraDir.z), 1.2f);
                 float B = saturate(A);
                 float C = dot(TransformedCameraDir.z, newUVsY);
                 float D = B * C  - 0.5f;
                 float E = D * 3;
                 float Mask = pow((1-t), -0.08f) * pow((t + 0.5f),0.33f);
                 float H = E * Mask;
                // float3 normalDir = normalWS * float3(1,1,0);
                // float3 Tilt = normalDir * H;
                //
               o.normalWS = normalInputs.normalWS;
                
                o.positionCS = positionInputs.positionCS;
                o.uv = v.uv;
                o.color =  Mask * float4(1,1,1,1);
                return o;
            }

            float4 frag (VertexOutput i) : SV_Target
            {
                 
                return i.color;
            }
            ENDHLSL
        }
    }
}
