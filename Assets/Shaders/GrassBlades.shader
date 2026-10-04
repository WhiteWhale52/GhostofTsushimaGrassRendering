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
            #include "PerlinNoise.hlsl"
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

            // ── WIND GLOBALS ─────────────────────────────────────────────────────────────
            // Set by GrassWindSystem.UploadToShader() every frame.
            // All shaders in the project share these — grass, cloth, particles, etc.

            float4 _WindDirection;       // xyz = world-space XZ direction (y=0), w = unused
            float  _WindStrength;        // current total strength (base + gust contribution)
            float  _WindSpeed;           // wave propagation speed across the field
            float  _WindTime;            // wind-local time (continuous, never resets)
            float  _NoiseScrollSpeed;    // how fast Perlin UV scrolls in wind direction
            float  _NoiseScale;          // spatial frequency of Perlin noise
            int    _NoiseFBMOctaves;     // fBm octave count (1–6)
            float  _GustStrength;        // maximum gust strength multiplier


            struct Vorticle
            {
                float4 positionAndRadius;       // xyz = world position, w = radius
                float4 directionAndStrength;    // xyz = normalised push direction, w = strength
                float4 decayAndPad;             // x = decayT [1=alive, 0=gone], yzw = padding
            };
            StructuredBuffer<Vorticle> _Vorticles;
            int _VorticleCount;

            float3 ComputeWindDisplacement(float3 worldRoot, float t, GrassBladeInstanceData blade)
            {
               float3 windDir = normalize(_WindDirection.xyz);

               float2 scrollUV = worldRoot.xz * _NoiseScale + _WindDirection.xz * _NoiseScrollSpeed * _WindTime;
                
               float noiseValue = FBM2D(scrollUV, _NoiseFBMOctaves, 2.0, 0.5);

               noiseValue = noiseValue / 0.87;
               
               float gustA    = sin(_WindTime * _GustStrength    + blade.windPhaseOffset);
               float gustB    = sin(_WindTime * _GustStrength * 0.7  + blade.windPhaseOffset * 1.3);
               float gustPeak = max(0.0, gustA * gustB);   // [0, 1]
                

               float windField = noiseValue + gustPeak * 0.4;

               // Per-blade windStrength [0.8, 1.2] set at population time adds variation:
               // some blades react more to wind than others (different stiffness in nature)
               float3 primaryDisp = windDir * windField * _WindStrength * blade.windStrength;
             
               float3 vorticleDisp = float3(0.0, 0.0, 0.0);

               [loop]
               for (int i = 0; i < _VorticleCount; i++)
               {
                    Vorticle v = _Vorticles[i];

                    float radius   = v.positionAndRadius.w;
                    float strength = v.directionAndStrength.w;
                    float decayT   = v.decayAndPad.x;

                    // Skip zeroed / fully-decayed slots
                    if (strength < 0.001 || decayT < 0.001 || radius < 0.001)
                        continue;

                    float3 vorticlePos = v.positionAndRadius.xyz;

                    // Only the XZ distance matters — vorticles affect blades regardless
                    // of height difference (a sword swing at chest height still pushes
                    // grass at ankle height)
                    float2 delta2D = worldRoot.xz - vorticlePos.xz;
                    float  dist    = length(delta2D);

                    if (dist >= radius)
                        continue;

                    // Smooth radial falloff: 1 at center, 0 at edge.
                    // smoothstep(0, 1, 1 - dist/radius) achieves this.
                    float normalizedDist = dist / radius;
                    float falloff        = smoothstep(1.0, 0.0, normalizedDist);

                    // Determine push direction
                    float3 pushDir;
                    float3 vorticleDir = v.directionAndStrength.xyz;
                    float  dirLen      = length(vorticleDir);

                    if (dirLen > 0.001)
                    {
                        pushDir = vorticleDir / dirLen; 
                    }
                    else
                    {
                        // Omni vorticle — explosion, impact
                        // Push outward from vorticle center in XZ plane
                        if (dist > 0.001)
                            pushDir = float3(delta2D.x / dist, 0.0, delta2D.y / dist);
                        else
                            pushDir = float3(0.0, 0.0, 0.0);   // blade is at exact center — no push
                    }

                    // Accumulate: strength × falloff × decay
                    // decayT fades the vorticle out over its lifetime
                    vorticleDisp += pushDir * strength * falloff * decayT;
                }
                float heightScale   = t * t;
                float stiffnessDamp = 1.0 - blade.stiffness * 0.5;

                float3 totalDisp = (primaryDisp + vorticleDisp) * heightScale * stiffnessDamp;

                return totalDisp;
            }


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
