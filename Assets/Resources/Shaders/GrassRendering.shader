Shader "Custom/GrassRendering"
{
    Properties
    {
        _DiffuseTex ("DiffuseTexture", 2D) = "white" {}
        _InteractionTex("Render Texture", 2D) = "black" {}
        _GrassColorTexture ("Grass Color Texture", 2D) = "white" {}
        _UpperColor("Upper Color", Color) =  (0,1,0,1)
        _LowerColor ("Lower Color", Color) = (.9,1,.2,1)
        _Height ("Height" , Float) = 2
        _Tilt ("Tilt", Float) = 0.2
        _Bend ("Bend", Float) = 2
        _Midpoint ("Midpoint", Range(0,1)) = 0.5
        _Width("Width", Float) = .02
        _ViewThickening ("View-dependent thickening", Float) = 0.4
        _FacingDirection ("Facing Direction", Vector) = (1,1,0,0)
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            
            struct VertInput
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct VertOutput
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD1;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            
            
            CBUFFER_START(UnityPerMaterial)
            float4 _UpperColor;
            float4 _LowerColor;
            float _Tilt;
            float _Bend;
            float _Midpoint;
            float _Height;
            float _Width;
            float _BlendFactor;
            float _ViewThickening;
            float2 _FacingDirection;
            CBUFFER_END


            UNITY_DOTS_INSTANCING_START(UserPropertyMetadata)
                 //TODO: Declare all the per-instance properties
             UNITY_DOTS_INSTANCING_END(UserPropertyMetadata)

            TEXTURE2D(_GrassColorTexture);
            SAMPLER(sampler_GrassColorTexture);
            
            float3 GetBezierCurvePoint(float3 P_0, float3 P_1, float3 P_2, float t)
            {
                return (1 - t) * (1 - t) * P_0 + 2 * t * (1 - t) * P_1 + t * t * P_2;
            }

           float3 GetBezierDerivative(float3 P0, float3 P1, float3 P2, float t)
            {
               return 2.0 * (1.0 - t) * (P1 - P0) + 2.0 * t * (P2 - P1);
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

             half3 ApplySingleDirectLight(Light light, half3 N, half3 V, half3 albedo, half mask, half positionY)
            {
                half3 H = normalize(light.direction + V);

                half directDiffuse = dot(N, light.direction) * 0.5 + 0.5;

                float directSpecular = saturate(dot(N,H));
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;
                directSpecular *= directSpecular;

                directSpecular *= positionY * 0.12;

                half3 lighting = light.color * (light.shadowAttenuation * light.distanceAttenuation);
                half3 result = (albedo * directDiffuse + directSpecular * (1-mask)) * lighting;

                return result; 
            }

             float3 CalculateLighting(float3 albedo, float3 positionWS, float3 N, float3 V, float mask, float positionY){

                half3 result = SampleSH(0) * albedo;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                result += ApplySingleDirectLight(mainLight, N, V, albedo, mask, positionY);

                int additionalLightsCount = GetAdditionalLightsCount();
                for (int i = 0; i < additionalLightsCount; ++i)
                {
                    Light light = GetAdditionalLight(i, positionWS);
                    result += ApplySingleDirectLight(light, N, V, albedo, mask, positionY);
                }

                return result;
            }


            VertOutput vert (VertInput v)
            {
                VertOutput o;
                float positionAlongBlade = v.uv.y;
                float w = v.uv.x;

                float3 P_0 = float3(0,0,0);
                float3 P_2 =  float3(_Height * cos(_Tilt),_Height * sin(_Tilt), 0);
                float3 MidPoint = (P_2 - P_0) * _Midpoint;
                float3 P_1 = float3(max(MidPoint.x - sin(_Tilt) * 0.5f * _Bend ,0), MidPoint.y + cos(_Tilt), 0);
                float3 curvedPos = GetBezierCurvePoint(P_0, P_1, P_2, positionAlongBlade);
                
                float2 facingDir = normalize(_FacingDirection); // make sure it's normalized
              
                float3 rotatedPos;
                rotatedPos.x = curvedPos.x * facingDir.x;
                rotatedPos.y = curvedPos.y;
                rotatedPos.z = curvedPos.x * facingDir.y;
                
                float2 orthogonalNormal = normalize(float2(-facingDir.y, facingDir.x));
                
                
               // float3 vertPos = float3(normalize(_FacingDirection.x) + curvedPos.x, curvedPos.y, normalize(_FacingDirection.y) + curvedPos.z);
                // TODO: Lerp between 15 verts to 7 vert look, the # of vertices is the same just verts move based on
                // distance
                // Previous attempt:
                // if (0 < t < 0.3) t = lerp(t, 0.3333, _BlendFactor)
                // if (0.3 < t < 0.6) t = lerp(t, 0.6667, _BlendFactor)
                // if (0.6 < t  ) t = lerp(t, 1, _BlendFactor)
                float2 stepDirection = (1-positionAlongBlade) * _Width * orthogonalNormal * (w-0.5f)*2;
                rotatedPos += float3(stepDirection.x,0,stepDirection.y);
                
                
                // Calculate tangent (derivative of the curve)
                float3 tangent = GetBezierDerivative(P_0, P_1, P_2, positionAlongBlade);
                tangent = normalize(tangent);

                // Transform tangent to world space
                float3 worldTangent = normalize(float3(
                    tangent.x * facingDir.x,
                    tangent.y,
                    tangent.x * facingDir.y
                ));

                // Ensure bitangent is orthogonal to tangent in world space
                float3 worldBitangent = normalize(float3(
                    -facingDir.y,  // Orthogonal to facingDir
                    0.0,
                    facingDir.x
                ));

                // Correct cross product order: tangent × bitangent = normal
                float3 vertexNormal = normalize(cross(worldTangent, worldBitangent));
              //  vertexNormal = normalize(cross(tangent,worldBitangent));
             //   float3 finalDisplacedPos = DisplaceVertByBezierCurve(curvedPos, w, _Width, t );
               // float3 finalDisplacedPos = DisplaceVertByBezierCurve(curvedPos, side, 3 ,t);
               // finalDisplacedPos = DisplaceVertByWindTexture(finalDisplacedPos);
                //finalDisplacedPos = DisplaceVertByInteraction(finalDisplacedPos);
                
                //TODO: Compute ortho-normal direction by getting the per-blade facing direction
                // flipping the x and z axis and negating one
                // Then step the vertices in that direction based on width
                
                //TODO: Define the vertex normal, by finding the derivative of the bezier curve
                // at the position and cross it with ortho-normal we found
                
                // TODO: Perform some bobbing up and down based on a sine wave, where
                // the phase offset is affected by the per-blade hash
                
                // TODO: Tilt the vertices normal to give the blades a more rounded natural look
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);

                

                const VertexPositionInputs positionInputs = GetVertexPositionInputs(rotatedPos);
                
                float sideSign = (v.uv.x - 0.5f) * 2;
                
                // Direction from vertex to camera
                float3 viewDir = normalize(_WorldSpaceCameraPos - positionInputs.positionWS);
                float sideFacing = dot(viewDir, orthogonalNormal);
                bool isFar = sign(sideSign) != sign(sideFacing);
                 float mask = saturate(pow((1-positionAlongBlade),0.5f)*0.7f+ 0.3f);
                 float3 offset = isFar ? vertexNormal * _ViewThickening * sideFacing : -vertexNormal * _ViewThickening;
                 offset *= mask;
                 // float B = viewDir.x * newUVsX;
                // float saturatedViewDirComponent = saturate(pow(abs(viewDir.x),0.5f)) ;
                // float C = saturatedViewDirComponent * B ;
                // float D = _ViewThickening * C * mask;
                // Alignment between camera view and blade side
             //   float sideAlignment = abs(dot(viewDir, OrthogonalNormalIn3D)); // [0,1]

                // Curve the blade outwards more when view is aligned with side
              //  float thicknessFactor = pow(sideAlignment, 0.5f); // can adjust exponent
                //float baseOffset = thicknessFactor * newUVsX;

                // Blend by height on blade
                //float H = saturate(baseOffset * _ViewThickening * (positionAlongBlade)* 0.9f + 0.1f);

                
                float3 normalDir = TransformObjectToWorldNormal(vertexNormal);
                //float3 Tilt = -vertexNormal * worldBitangent * D ;
              //  float3 Tilt = -dot(vertexNormal,viewDir) * vertexNormal * _ViewThickening;
                 
               //o.color =  float4(-dot(normalDir,viewDir),0,0, 1); 

                // Apply displacement
                float3 displaced = rotatedPos; // <- should be 0, not 1!
                 offset = TransformWorldToObject(offset);
                o.positionCS = TransformObjectToHClip(displaced + offset);
                o.normalWS = normalDir;
                o.uv = v.uv;

                o.color= float4(0,0,0,1);
                return o;
            }

            float4 frag (VertOutput i) : SV_Target
            {
               //return i.color;
                //return float4(0,i.color.g,0,1);
            //    return float4(i.normalWS * 0.5f + 0.5f,1);
                
                float4 col = half4(SAMPLE_TEXTURE2D(_GrassColorTexture,sampler_GrassColorTexture, i.uv));
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos - (i.positionCS));
                float NdotL = saturate(-dot(i.normalWS, lightDir));
                float3 baseColor = lerp( _LowerColor,_UpperColor, col.r).rgb * NdotL * _MainLightColor.rgb;
              //  float4 lerpedColor = lerp(_LowerColor, _UpperColor, i.uv.y);
              //  float4 baseColor = lerpedColor ;
                // TODO: Add the diffuse colors
                // Two textures are combined:
                // A 1D texture (like gloss) for blade-width variation (e.g., a central vein).
                // A 2D texture where:
                // V-coordinate = Color gradient along blade length (dark at base, light at tip).
                // U-coordinate = Randomized per clump (not per blade), allowing artist-controlled color variation across fields.
                // This avoids noisy randomness while maintaining a natural, "painted" look.
                
                // TODO: Translucency: Simulates light scattering through grass.
                // Higher at the thick base, fading toward the tip.
                return float4(baseColor,1);
            }
            ENDHLSL
        }
    }
}
