Shader "Custom/ProceduralGrassBlades_BRG"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.2, 0.4, 0.1, 1)
        _TipColor("Tip Color", Color) = (0.4, 0.6, 0.2, 1)
        _WindStrength("Wind Strength", Range(0,2)) = 1
        _WindTexture("Wind Texture", 2D) = "white" {}
    }

    SubShader
    {
        Tags { 
            "RenderType" = "Opaque" 
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        Cull Off

        Pass
        {
            Name "GrassForward"
            Tags { "LightMode" = "UniversalForward" }
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            // ========== CRITICAL: ONLY USE DOTS INSTANCING ==========
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma instancing_options renderinglayer
            // DON'T use: #pragma multi_compile_instancing (that's GPU Instancing, not BRG)

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            // ========== BRG INSTANCE DATA DECLARATION ==========
            
            #ifdef DOTS_INSTANCING_ON
            
            // This is the CORRECT way to declare per-instance data for BRG
            UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
                // Transform (16 bytes)
                UNITY_DOTS_INSTANCED_PROP(float3, _Position)
                UNITY_DOTS_INSTANCED_PROP(float,  _FacingAngle)
                
                // Shape (16 bytes)
                UNITY_DOTS_INSTANCED_PROP(float, _Height)
                UNITY_DOTS_INSTANCED_PROP(float, _Width)
                UNITY_DOTS_INSTANCED_PROP(float, _Curvature)
                UNITY_DOTS_INSTANCED_PROP(float, _Lean)
                
                // Variation (16 bytes)
                UNITY_DOTS_INSTANCED_PROP(float, _ShapeProfileID)
                UNITY_DOTS_INSTANCED_PROP(float, _ColorSeed)
                UNITY_DOTS_INSTANCED_PROP(float, _BladeHash)
                UNITY_DOTS_INSTANCED_PROP(float, _Stiffness)
                
                // Animation (16 bytes)
                UNITY_DOTS_INSTANCED_PROP(float, _WindPhaseOffset)
                UNITY_DOTS_INSTANCED_PROP(float, _WindStrengthMult)
                // No need to declare padding
            UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
            
            // Macro to read instance data (Unity provides this)
            #define LOAD_BLADE_INSTANCE_DATA(propertyName) \
                UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata_##propertyName)
            
            #endif

            // ========== VERTEX/FRAGMENT STRUCTURES ==========
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID  // ← Required for BRG
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 color : TEXCOORD3;
                float ambientOcclusion : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID  // ← Pass through to fragment
            };

            // ========== PROPERTIES ==========
            
            TEXTURE2D(_WindTexture);
            SAMPLER(sampler_WindTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _TipColor;
                float _WindStrength;
            CBUFFER_END

            // ========== HERMITE CURVE MATH (unchanged) ==========
            
                float3 HermitePosition(float3 p0, float3 p1, float3 m0, float3 m1, float t)
                {
                    float t2 = t * t;
                    float t3 = t2 * t;
                
                    float h00 = 2.0 * t3 - 3.0 * t2 + 1.0;
                    float h10 = t3 - 2.0 * t2 + t;
                    float h01 = -2.0 * t3 + 3.0 * t2;
                    float h11 = t3 - t2;
                
                    return h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;
                }
            
                float3 HermiteTangent(float3 p0, float3 p1, float3 m0, float3 m1, float t)
                {
                    float t2 = t * t;
                
                    float h00_prime = 6.0 * t2 - 6.0 * t;
                    float h10_prime = 3.0 * t2 - 4.0 * t + 1.0;
                    float h01_prime = -6.0 * t2 + 6.0 * t;
                    float h11_prime = 3.0 * t2 - 2.0 * t;
                
                    return h00_prime * p0 + h10_prime * m0 + h01_prime * p1 + h11_prime * m1;
                }

            // ========== HELPER FUNCTIONS (unchanged) ==========
            
                void BuildBladeFrame(float3 tangent, float facingAngle, 
                                    out float3 T, out float3 S, out float3 N)
                {
                    T = normalize(tangent);
                    float3 facingDir = float3(sin(facingAngle), 0, cos(facingAngle));
                    S = normalize(cross(facingDir, T));
                
                    if (length(S) < 0.01)
                    {
                        S = normalize(cross(float3(0, 1, 0), T));
                    }
                
                    N = normalize(cross(T, S));
                }
            
                float3 SampleWind(float3 worldPos, float time, float phase, float stiffness, float t)
                {
                    float2 windUV = worldPos.xz * 0.01;
                    float4 windData = SAMPLE_TEXTURE2D_LOD(_WindTexture, sampler_WindTexture, windUV, 0);
                
                    float2 windDir = windData.rg * 2.0 - 1.0;
                    float windStrength = windData.b;
                
                    float timePhase = time * 2.0 + phase;
                    float sway = sin(timePhase) * 0.5 + 0.5;
                
                    float heightFactor = t * t;
                    float bendFactor = 1.0 - stiffness;
                
                    float totalWind = windStrength * sway * heightFactor * bendFactor * _WindStrength;
                
                    return float3(windDir.x, 0, windDir.y) * totalWind * 0.5;
                }
            
                float Hash(float seed)
                {
                    return frac(sin(seed * 12.9898) * 43758.5453);
                }

            // ========== VERTEX SHADER (MODIFIED FOR BRG) ==========
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                // ===== SETUP INSTANCE ID (REQUIRED FOR BRG) =====
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                
                #ifdef DOTS_INSTANCING_ON
                
                // ===== READ INSTANCE DATA USING BRG MACROS =====
                float3 bladePosition = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float3, Metadata__Position);
                float facingAngle = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__FacingAngle);
                float height = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__Height);
                float width = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__Width);
                float curvature = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__Curvature);
                float lean = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__Lean);
                float colorSeed = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__ColorSeed);
                float bladeHash = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__BladeHash);
                float stiffness = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__Stiffness);
                float windPhaseOffset = UNITY_ACCESS_DOTS_INSTANCED_PROP_FROM_MACRO(float, Metadata__WindPhaseOffset);
                
                #else
                
                // Fallback for non-BRG rendering (shouldn't happen, but safe)
                float3 bladePosition = float3(0, 0, 0);
                float facingAngle = 0;
                float height = 1.0;
                float width = 0.03;
                float curvature = 0;
                float lean = 0;
                float colorSeed = 0.5;
                float bladeHash = 0.5;
                float stiffness = 0.5;
                float windPhaseOffset = 0;
                
                #endif
                
                // ===== EARLY EXIT FOR INVALID BLADES =====
                if (height <= 0.0)
                {
                    output.positionCS = float4(0, 0, 0, 1);
                    output.positionWS = float3(0, 0, 0);
                    output.normalWS = float3(0, 1, 0);
                    output.uv = float2(0, 0);
                    output.color = float3(0, 0, 0);
                    output.ambientOcclusion = 1;
                    return output;
                }
                
                // ===== EXTRACT PARAMETERS FROM UV =====
                float t = input.uv.y;
                float side = input.uv.x * 2.0 - 1.0;
                
                // ===== BUILD HERMITE CURVE =====
                float3 p0 = bladePosition;
                float3 p1 = bladePosition + float3(0, height, 0);
                
                float3 leanDir = float3(sin(facingAngle), 0, cos(facingAngle));
                float3 m0 = normalize(float3(0, 1, 0) + leanDir * lean) * height * 0.5;
                
                float3 curveDir = leanDir * curvature;
                float3 m1 = normalize(float3(0, 1, 0) + curveDir) * height * 0.3;
                
                // ===== EVALUATE CURVE =====
                float3 centerPos = HermitePosition(p0, p1, m0, m1, t);
                float3 tangent = HermiteTangent(p0, p1, m0, m1, t);
                
                // ===== BUILD FRAME =====
                float3 T, S, N;
                BuildBladeFrame(tangent, facingAngle, T, S, N);
                
                // ===== COMPUTE WIDTH =====
                float widthTaper = pow(max(0.0,1.0 - t), 1.3);
                widthTaper *= (0.9 + 0.2 * bladeHash);
                float currentWidth = width * widthTaper;
                
                // ===== OFFSET =====
                float3 worldPos = centerPos + S * (side * currentWidth);
                
                // ===== WIND =====
                float3 windOffset = SampleWind(worldPos, _Time.y, windPhaseOffset, stiffness, t);
                worldPos += windOffset;
                
                // ===== NORMAL =====
                float3 finalNormal = N;
                
                // ===== COLOR =====
                float3 bladeColor = lerp(_BaseColor.rgb, _TipColor.rgb, t);
                float colorNoise = Hash(colorSeed);
                bladeColor += (colorNoise - 0.5) * 0.1 * float3(0.1, 0.05, 0.02);
                
                // ===== AO =====
                float ao = lerp(0.6, 1.0, t);
                
                // ===== OUTPUT =====
                output.positionWS = worldPos;
                output.positionCS = TransformWorldToHClip(worldPos);
                output.normalWS = TransformObjectToWorldNormal(finalNormal);
                output.uv = input.uv;
                output.color = bladeColor;
                output.ambientOcclusion = ao;
                
                return output;
            }

            // ========== FRAGMENT SHADER (unchanged) ==========
            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);  // ← Add this
                
                float3 N = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 V = normalize(GetCameraPositionWS() - input.positionWS);
                
                float NdotL = dot(N, L);
                float wrap = 0.5;
                float diffuse = saturate((NdotL + wrap) / (1.0 + wrap));
                
                float3 H = normalize(L + N * 0.5);
                float VdotH = saturate(dot(V, -H));
                float translucent = pow(VdotH, 4.0) * 0.3;
                
                float3 ambient = input.color * 0.3;
                float3 diffuseLight = input.color * diffuse * mainLight.color.rgb;
                float3 translucentLight = input.color * translucent * mainLight.color.rgb;
                
                float3 finalColor = (ambient + diffuseLight + translucentLight) * input.ambientOcclusion;
                
                float edgeFade = abs(input.uv.x * 2.0 - 1.0);
                float alpha = smoothstep(1.0, 0.7, edgeFade);
                
                return half4(finalColor, alpha);
            }
            
            ENDHLSL
        }
    }
    
    FallBack "Hidden/InternalErrorShader"
}