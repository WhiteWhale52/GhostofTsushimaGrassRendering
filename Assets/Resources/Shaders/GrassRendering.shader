Shader "Custom/GrassRendering"
{
    Properties
    {
        _DiffuseTex ("DiffuseTexture", 2D) = "white" {}
        _InteractionTex("Render Texture", 2D) = "black" {}
        _UpperColor("Upper Color", Color) =  (0,1,0,1)
        _LowerColor ("Lower Color", Color) = (.9,1,.2,1)
        _Height ("Height" , Float) = 2
        _Tilt ("Tilt", Range(0, 1.57)) = 0.2
        _Bend ("Bend", Range(0, 8)) = 2
        _Midpoint ("Midpoint", Range(0, 1)) = 0.5
        _Width("Width", Float) = 2
        _BlendFactor ("Blend Factor", Range(0,1)) = .4
        //TODO: Use a diffuse texture instead of colors
        //TODO: Use a RenderTexture to make player interaction
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        Pass
        {
            Name "Rendering Grass"
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
            
            struct VertInput
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float2 curveParams : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertOutput
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;

            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
                //TODO: Declare all the per-instance properties
            UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)
            
            CBUFFER_START(UnityPerMaterial)
            ByteAddressBuffer _InstanceData;
            float4 _UpperColor;
            float4 _LowerColor;
            float _Tilt;
            float _Bend;
            float _Midpoint;
            float _Height;
            float _Width;
            float _BlendFactor;
            CBUFFER_END

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

            
            float3 GetBezierCurvePoint(float3 P_0, float3 P_1, float3 P_2, float t)
            {
                return (1 - t) * (1 - t) * P_0 + 2 * t * (1 - t) * P_1 + t * t * P_2;
            }
            
            float3 DisplaceVertByBezierCurve(float3 originalPos, float side, float width, float t)
            {
                 float3 displacedPos = originalPos;
                displacedPos.z += (1-t) * _Width * side;
                return displacedPos;
            }

             float3 DisplaceVertByBezierCurve1(float3 originalPos, float w, float t)
            {
                float3 displacedPos = originalPos;
                 float A = (1-t) * (w-0.5f);
                float B = (0.15f + t) * A;
                float C = _Width * B;
                float D = (50 - 100 * w) + C;
                displacedPos.z += D;
                return displacedPos;
            }

            float3 DisplaceVertByWindTexture(float3 originalPos)
            {
                float3 displacedPos = float3(0,0,0);
                return displacedPos;
            }

            float3 DisplaceVertByInteraction(float3 originalPos)
            {
                float3 displacedPos = float3(0,0,0);
                return displacedPos;
            };

            VertOutput vert (VertInput v, uint instanceID : SV_InstanceID)
            {
                VertOutput o;
                
                float t = v.uv.y;
                float w = v.uv.x;
                float A = (1-t) * (w-0.5f);
                float B = (0.15f + t) * A;
                float C = _Width * B;
                float D = (50 - 100 * w) + C;
                float side = v.curveParams.x;   

                float3 P_0 = float3(0,0,0);
                float3 P_2 =  float3(_Height * cos(_Tilt),_Height * sin(_Tilt), 0);

                float3 MidPoint = (P_2 - P_0) * _Midpoint;
                float3 P_1 = float3(max(MidPoint.x - sin(_Tilt) * 0.5f * _Bend ,0), MidPoint.y + cos(_Tilt), 0);


                float3 curvedPos = GetBezierCurvePoint(P_0, P_1, P_2, t);

                // TODO: Lerp between 15 verts to 7 vert look, the # of vertices is the same just verts move based on
                // distance
                // Previous attempt:
                // if (0 < t < 0.3) t = lerp(t, 0.3333, _BlendFactor)
                // if (0.3 < t < 0.6) t = lerp(t, 0.6667, _BlendFactor)
                // if (0.6 < t  ) t = lerp(t, 1, _BlendFactor)
                
             //   float3 finalDisplacedPos = DisplaceVertByBezierCurve(curvedPos, side, _Width, t );
                float3 finalDisplacedPos = DisplaceVertByBezierCurve1(curvedPos, w ,t);
                finalDisplacedPos = DisplaceVertByWindTexture(finalDisplacedPos);
                finalDisplacedPos = DisplaceVertByInteraction(finalDisplacedPos);
                
                //TODO: Compute ortho-normal direction by getting the per-blade facing direction
                // flipping the x and z axis and negating one
                // Then step the vertices in that direction based on width
                
                //TODO: Define the vertex normal, by finding the derivative of the bezier curve
                // at the position and cross it with ortho-normal we found
                
                // TODO: Perform some bobbing up and down based on a sine wave, where
                // the phase offset is affected by the per-blade hash
                
                // TODO: Tilt the vertices normal to give the blades a more rounded natural look

                uint byteOffset = instanceID * 128;
                
                o.vertex = TransformObjectToHClip(finalDisplacedPos);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                
                return o;
            }

            float4 frag (VertOutput i) : SV_Target
            {
                float4 col = tex2D(_MainTex, i.uv);
                // TODO: Add the diffuse colors
                // Two textures are combined:
                // A 1D texture (like gloss) for blade-width variation (e.g., a central vein).
                // A 2D texture where:
                // V-coordinate = Color gradient along blade length (dark at base, light at tip).
                // U-coordinate = Randomized per clump (not per blade), allowing artist-controlled color variation across fields.
                // This avoids noisy randomness while maintaining a natural, "painted" look.
                
                // TODO: Translucency: Simulates light scattering through grass.
                // Higher at the thick base, fading toward the tip.
                return lerp( _LowerColor,_UpperColor, i.uv.y);
            }
            ENDHLSL
        }
    }
}
