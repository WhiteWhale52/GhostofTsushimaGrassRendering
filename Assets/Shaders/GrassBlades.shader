Shader "Custom/GrassBlades"
{
   

    SubShader
    {
        Tags 
        {
        "RenderType" = "Opaque" 
        "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off

        Pass
        {
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma target   4.5
            #pragma multi_compile_instancing
            #pragma instancing_options procedural:SetupInstanceData

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct GrassBladeInstanceData
            {
                float4 positionAndAngle;
                float  heightMultiplier;    // was 'height'
                float  widthMultiplier;     // was 'width'
                float  lean;
                float  curvatureStrength;   // was missing
                float  bendDirection;
                float  bendAmount;          // was 'bendStrength'
                int    shapeProfileID;
                float  colorVariationSeed;
                float  bladeHash;
                float  stiffness;
                float  windStrength;
                float  windPhaseOffset;
                uint   voronoiCellID;
                float  padding;
            };

            StructuredBuffer<GrassBladeInstanceData> _BladeData;

            void SetupInstanceData() {
            
            }
                
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 color : TEXCOORD1;
             //   UNITY_VERTEX_INPUT_INSTANCE_ID
            };


            Varyings vert(Attributes IN)
            {
                Varyings OUT;

              //  UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                UNITY_SETUP_INSTANCE_ID(IN);
               // UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                uint instanceID = 0;

                #if defined(UNITY_PROCEDURAL_INSTANCING_ENABLED)
                    instanceID = unity_InstanceID;
                #endif

                GrassBladeInstanceData blade = _BladeData[instanceID];

                float3 worldOrigin = blade.positionAndAngle.xyz;
                float  angle       = blade.positionAndAngle.w;
                float  height      = blade.heightMultiplier;
                float  width       = blade.widthMultiplier;

                // ── UV encodes blade-local position ────────────────────────
                float side      = IN.uv.x * 2.0 - 1.0;  // -1=left, +1=right
                float t         = IN.uv.y;                //  0=root,  1=tip

                // ── Reconstruct blade-local position ───────────────────────
                // Width tapers toward tip
                float taperWidth = width * 0.05 * (1.0 - t * 0.6);

                // Lean driven by curvature — simple quadratic for now
                // Replace with Hermite curve evaluation later
                float lateralLean = blade.lean + blade.curvatureStrength * t * t;

                float3 localPos = float3(
                    side * taperWidth + lateralLean * t,
                    t * height,
                    0.0
                );

                // ── Rotate blade around Y by facing angle ──────────────────
                float cosA = cos(angle), sinA = sin(angle);
                float3 rotatedPos = float3(
                    localPos.x * cosA - localPos.z * sinA,
                    localPos.y,
                    localPos.x * sinA + localPos.z * cosA
                );

                // ── Wind displacement (root fixed at t=0) ──────────────────
                float windWave = sin(_Time.y * 2.0 + blade.windPhaseOffset
                                     + worldOrigin.x * 0.3 + worldOrigin.z * 0.3);
                rotatedPos.x  += windWave * blade.windStrength * 0.3 * t;

                // ── Character interaction ──────────────────────────────────
                float bendCos = cos(blade.bendDirection), bendSin = sin(blade.bendDirection);
                rotatedPos.x  += bendCos * blade.bendAmount * t;
                rotatedPos.z  += bendSin * blade.bendAmount * t;

                float3 worldPos = worldOrigin + rotatedPos;

                // ── Output ─────────────────────────────────────────────────
                OUT.positionCS = TransformWorldToHClip(worldPos);
                OUT.uv    = IN.uv;

                // Height-based color gradient (root darker, tip lighter)
                float3 rootColor = float3(0.1, 0.5, 0.1);
                float3 tipColor  = float3(0.3, 0.9, 0.2);
                OUT.color = lerp(rootColor, tipColor, t) + blade.colorVariationSeed * 0.1;

                return OUT;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                return float4(IN.color, 1.0);
            }
            ENDHLSL
        }
    }
}
