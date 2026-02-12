# GPU-SIDE IMPLEMENTATION GUIDE (Shaders & Compute)
## What Belongs on the GPU and Why

---

## PHILOSOPHY: GPU Responsibilities

The GPU should handle:
- **Curve evaluation** (Hermite/Bezier position and tangent)
- **Geometry construction** (building blade ribbons from parameters)
- **Normal computation** (from curve derivatives)
- **Wind deformation** (per-vertex displacement)
- **Lighting calculations** (fragment shading)
- **Texture sampling** (wind, density, color variation)

The GPU should NEVER:
- ❌ Make spatial decisions (which chunks exist)
- ❌ Manage memory allocation
- ❌ Handle chunk streaming
- ❌ Decide LOD (receives LOD as input)

---

## 1. STRUCTURED BUFFER INPUT (GPU Receives from CPU)

### Instance Data Buffer
```pseudocode
// This matches the CPU-side BladeInstanceData struct EXACTLY
// GPU reads this data, never writes to it

STRUCTURED_BUFFER BladeInstances : register(t0):
    STRUCT BladeInstanceData:
        // Transform (16 bytes)
        float3 position
        float facingAngle
        
        // Shape (16 bytes)
        float height
        float width
        float curvature
        float lean
        
        // Variation (16 bytes)
        int shapeProfileID
        float colorVariationSeed
        float randomSeed
        float stiffness
        
        // Animation (16 bytes)
        float windPhaseOffset
        float swayFrequency
        float2 padding

// Total: 64 bytes per instance
```

### Shape Profile Buffer (Optional Advanced Feature)
```pseudocode
// Stores control points for different blade shapes
// Each shape = 4 control points for cubic Bezier

STRUCTURED_BUFFER ShapeProfiles : register(t1):
    STRUCT ShapeProfile:
        float3 p0  // Root offset
        float3 p1  // First control point
        float3 p2  // Second control point
        float3 p3  // Tip offset

// Usage: ShapeProfiles[blade.shapeProfileID]
```

---

## 2. GLOBAL SHADER PARAMETERS

### Textures and Samplers
```pseudocode
// Wind texture (updated by CPU compute shader)
TEXTURE2D(_WindTexture)
SAMPLER(sampler_WindTexture)

// Density texture array (one slice per chunk)
TEXTURE2D_ARRAY(_DensityMapArray)
SAMPLER(sampler_DensityMapArray)

// Color variation lookup
TEXTURE2D(_ColorVariationTex)
SAMPLER(sampler_ColorVariationTex)
```

### Global Parameters
```pseudocode
// Set by CPU once per frame
CBUFFER_START(GrassGlobals)
    float _Time                    // Current time for animation
    float _WindStrength            // Global wind multiplier
    float2 _WindDirection          // Global wind direction (XZ)
    float _WindSpeed               // Wind animation speed
    
    float3 _CameraPosition         // For LOD/distance calculations
    
    // Lighting
    float3 _LightDirection         // Main directional light
    float3 _LightColor
    float _AmbientStrength
    
    // Grass material properties
    float3 _GrassColorBase
    float3 _GrassColorTip
    float _ColorVariationStrength
    float _Translucency            // Subsurface scattering amount
    
CBUFFER_END
```

---

## 3. VERTEX SHADER INPUT/OUTPUT

### Vertex Input (From Mesh)
```pseudocode
STRUCT VertexInput:
    float3 positionOS : POSITION      // Object-space position (from template mesh)
    float3 normalOS : NORMAL          // Template normal (not used much)
    float2 uv : TEXCOORD0             // .x = side, .y = t parameter
    uint instanceID : SV_InstanceID   // Automatic - which blade this vertex belongs to
    uint vertexID : SV_VertexID       // Automatic - which vertex within mesh


// Note: The template mesh has minimal data
// Most computation happens in shader
```

### Vertex Output → Fragment Input
```pseudocode
STRUCT VertexToFragment:
    float4 positionCS : SV_POSITION   // Clip-space position (required)
    
    float3 positionWS : TEXCOORD0     // World-space position
    float3 normalWS : TEXCOORD1       // World-space normal
    float3 tangentWS : TEXCOORD2      // World-space tangent (for anisotropic lighting)
    
    float2 uv : TEXCOORD3             // Pass through UV (side, t)
    
    float bladeHeight : TEXCOORD4     // Total blade height (for color gradient)
    float randomSeed : TEXCOORD5      // For per-blade variation
    
    // Optional
    float3 viewDirWS : TEXCOORD6      // Direction to camera
    float fogFactor : TEXCOORD7       // Pre-computed fog
```

---

## 4. HERMITE CURVE MATHEMATICS (GPU Core)

### Hermite Position Evaluation
```pseudocode
FUNCTION HermitePosition(
    float3 p0,        // Start point
    float3 p1,        // End point  
    float3 m0,        // Start tangent
    float3 m1,        // End tangent
    float t           // Parameter [0-1]
) -> float3:
    
    // Pre-compute powers
    float t2 = t * t
    float t3 = t2 * t
    
    // Hermite basis functions
    float h00 = 2.0 * t3 - 3.0 * t2 + 1.0
    float h10 = t3 - 2.0 * t2 + t
    float h01 = -2.0 * t3 + 3.0 * t2
    float h11 = t3 - t2
    
    // Interpolate
    RETURN h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1
```

### Hermite Tangent (First Derivative)
```pseudocode
FUNCTION HermiteTangent(
    float3 p0, float3 p1, float3 m0, float3 m1, float t
) -> float3:
    
    float t2 = t * t
    
    // First derivative of basis functions
    float h00_prime = 6.0 * t2 - 6.0 * t
    float h10_prime = 3.0 * t2 - 4.0 * t + 1.0
    float h01_prime = -6.0 * t2 + 6.0 * t
    float h11_prime = 3.0 * t2 - 2.0 * t
    
    RETURN h00_prime * p0 + h10_prime * m0 + h01_prime * p1 + h11_prime * m1
```

### Building Blade Frame (Tangent, Bitangent, Normal)
```pseudocode
FUNCTION ComputeBladeFrame(float3 tangent, float3 worldUp) -> (float3, float3, float3):
    // Normalize tangent
    tangent = normalize(tangent)
    
    // Handle edge case: tangent parallel to worldUp
    IF abs(dot(tangent, worldUp)) > 0.95:
        worldUp = float3(1, 0, 0)  // Fallback
    
    // Compute side direction (perpendicular to tangent)
    float3 side = normalize(cross(worldUp, tangent))
    
    // Compute normal (perpendicular to both)
    float3 normal = normalize(cross(tangent, side))
    
    RETURN (tangent, side, normal)


// Alternative: Use blade facing direction instead of worldUp
FUNCTION ComputeBladeFrameWithFacing(float3 tangent, float facingAngle) -> (float3, float3, float3):
    tangent = normalize(tangent)
    
    // Compute facing direction in XZ plane
    float3 facingDir = float3(sin(facingAngle), 0, cos(facingAngle))
    
    // Side perpendicular to tangent, in facing direction
    float3 side = normalize(cross(facingDir, tangent))
    
    // If cross product near zero, use worldUp fallback
    IF length(side) < 0.01:
        side = normalize(cross(float3(0, 1, 0), tangent))
    
    float3 normal = normalize(cross(tangent, side))
    
    RETURN (tangent, side, normal)
```

---

## 5. VERTEX SHADER MAIN LOGIC

### Complete Vertex Shader
```pseudocode
FUNCTION VertexShader(VertexInput input) -> VertexToFragment:
    
    // ===== 1. FETCH INSTANCE DATA =====
    BladeInstanceData blade = BladeInstances[input.instanceID]
    
    // Early exit for invalid blades
    IF blade.height <= 0:
        // Return degenerate triangle (all vertices at origin)
        VertexToFragment output
        output.positionCS = float4(0, 0, 0, 1)
        RETURN output
    
    
    // ===== 2. EXTRACT UV PARAMETERS =====
    float t = input.uv.y            // Height along blade [0 = base, 1 = tip]
    float side = input.uv.x * 2 - 1 // Convert [0,1] to [-1,1] for left/right
    
    
    // ===== 3. BUILD HERMITE CURVE =====
    // Root point
    float3 p0 = blade.position
    
    // Tip point (directly above root, will be curved by tangents)
    float3 p1 = blade.position + float3(0, blade.height, 0)
    
    // Root tangent (grows upward with slight lean)
    float3 leanDir = float3(sin(blade.facingAngle), 0, cos(blade.facingAngle))
    float3 m0 = normalize(float3(0, 1, 0) + leanDir * blade.lean) * blade.height * 0.5
    
    // Tip tangent (curves based on curvature parameter)
    float3 curveDir = leanDir * blade.curvature
    float3 m1 = normalize(float3(0, 1, 0) + curveDir) * blade.height * 0.3
    
    
    // ===== 4. EVALUATE CURVE AT t =====
    float3 centerPos = HermitePosition(p0, p1, m0, m1, t)
    float3 tangent = HermiteTangent(p0, p1, m0, m1, t)
    
    
    // ===== 5. BUILD LOCAL FRAME =====
    (float3 tangentNorm, float3 sideDir, float3 normal) = 
        ComputeBladeFrameWithFacing(tangent, blade.facingAngle)
    
    
    // ===== 6. COMPUTE WIDTH (TAPER) =====
    // Wider at base, narrow at tip
    float widthFactor = pow(1.0 - t, 1.3)  // Nonlinear taper
    
    // Optional: Add slight width variation per blade
    widthFactor *= (0.9 + 0.2 * blade.randomSeed)
    
    float currentWidth = blade.width * widthFactor
    
    
    // ===== 7. OFFSET TO LEFT/RIGHT EDGE =====
    float3 worldPos = centerPos + sideDir * (side * currentWidth)
    
    
    // ===== 8. APPLY WIND DEFORMATION =====
    float3 windOffset = SampleWindDeformation(worldPos, blade, t)
    worldPos += windOffset
    
    
    // ===== 9. COMPUTE FINAL NORMAL =====
    // Option A: Keep geometric normal
    float3 finalNormal = normal
    
    // Option B: Blend normal toward blade facing (makes it look rounder)
    float3 fakeRoundNormal = normalize(normal + sideDir * (side * 0.3))
    finalNormal = normalize(lerp(normal, fakeRoundNormal, 0.4))
    
    
    // ===== 10. OUTPUT =====
    VertexToFragment output
    
    output.positionWS = worldPos
    output.positionCS = TransformWorldToHClip(worldPos)  // Unity function
    
    output.normalWS = finalNormal
    output.tangentWS = tangentNorm
    
    output.uv = input.uv
    output.bladeHeight = blade.height
    output.randomSeed = blade.randomSeed
    
    output.viewDirWS = normalize(_CameraPosition - worldPos)
    
    // Optional: Compute fog
    output.fogFactor = ComputeFogFactor(worldPos)
    
    RETURN output
```

---

## 6. WIND SYSTEM (GPU Deformation)

### Wind Sampling Function
```pseudocode
FUNCTION SampleWindDeformation(
    float3 worldPos,
    BladeInstanceData blade,
    float t  // Height parameter [0-1]
) -> float3:
    
    // ===== 1. SAMPLE WIND TEXTURE =====
    // Use world position as UV (tiling)
    float2 windUV = worldPos.xz * 0.01  // Scale factor controls wind pattern size
    
    // Sample wind texture (R=dirX, G=dirZ, B=strength)
    float4 windSample = SAMPLE_TEXTURE2D(_WindTexture, sampler_WindTexture, windUV)
    
    // Decode wind direction
    float2 windDir = windSample.rg * 2.0 - 1.0  // Remap [0,1] to [-1,1]
    float windStrength = windSample.b
    
    
    // ===== 2. TIME-BASED ANIMATION =====
    // Add time offset with per-blade phase
    float timePhase = _Time * _WindSpeed * blade.swayFrequency + blade.windPhaseOffset
    
    // Sine wave for smooth back-and-forth motion
    float swayAmount = sin(timePhase) * 0.5 + 0.5  // [0-1]
    
    
    // ===== 3. HEIGHT ATTENUATION =====
    // More wind at tip, less at base
    float heightFactor = t * t  // Quadratic falloff
    
    
    // ===== 4. STIFFNESS =====
    // Stiffer blades bend less
    float bendFactor = (1.0 - blade.stiffness)
    
    
    // ===== 5. COMPUTE FINAL OFFSET =====
    float3 windOffset3D = float3(windDir.x, 0, windDir.y)
    
    float totalWindStrength = 
        windStrength *           // From texture
        swayAmount *             // Time animation
        heightFactor *           // Height falloff
        bendFactor *             // Blade stiffness
        _WindStrength            // Global multiplier
    
    float3 finalOffset = windOffset3D * totalWindStrength * 0.5  // Scale to reasonable range
    
    
    // ===== 6. OPTIONAL: ADD HIGH-FREQUENCY FLUTTER =====
    // Small, fast motion at blade tip
    IF t > 0.7:
        float flutter = sin(timePhase * 8.0 + blade.randomSeed * 10.0) * 0.02
        finalOffset.y += flutter * (t - 0.7) * 3.0  // Only affect top 30%
    
    
    RETURN finalOffset
```

### Alternative: Procedural Wind (No Texture)
```pseudocode
FUNCTION ProceduralWind(float3 worldPos, BladeInstanceData blade, float t) -> float3:
    // Generate wind using math instead of texture
    
    float time = _Time * _WindSpeed
    
    // Layered sine waves for wind
    float wind1 = sin(worldPos.x * 0.1 + time + blade.windPhaseOffset)
    float wind2 = sin(worldPos.z * 0.15 - time * 0.7 + blade.randomSeed * 6.28)
    
    float windStrength = (wind1 + wind2 * 0.5) * 0.5
    
    // Wind direction
    float2 windDir = normalize(_WindDirection)
    
    // Height and stiffness attenuation
    float heightFactor = t * t
    float bendFactor = 1.0 - blade.stiffness
    
    // Final offset
    float3 offset = float3(windDir.x, 0, windDir.y) * 
                    windStrength * 
                    heightFactor * 
                    bendFactor * 
                    _WindStrength * 
                    0.3
    
    RETURN offset
```

---

## 7. FRAGMENT SHADER (LIGHTING & COLOR)

### Fragment Shader Main
```pseudocode
FUNCTION FragmentShader(VertexToFragment input) -> float4:
    
    // ===== 1. BASE COLOR =====
    // Gradient from base to tip
    float t = input.uv.y
    float3 baseColor = lerp(_GrassColorBase, _GrassColorTip, t)
    
    // Add per-blade color variation
    float colorNoise = Hash(input.randomSeed)  // Simple hash function
    baseColor += (colorNoise - 0.5) * _ColorVariationStrength * float3(0.1, 0.05, 0.02)
    
    
    // ===== 2. LIGHTING SETUP =====
    float3 N = normalize(input.normalWS)
    float3 L = normalize(_LightDirection)
    float3 V = normalize(input.viewDirWS)
    
    
    // ===== 3. DIFFUSE LIGHTING =====
    // Standard Lambertian
    float NdotL = dot(N, L)
    
    // Wrap lighting (softer, more suitable for foliage)
    float wrapAmount = 0.5
    float diffuse = saturate((NdotL + wrapAmount) / (1.0 + wrapAmount))
    
    
    // ===== 4. TRANSLUCENCY (SUBSURFACE SCATTERING) =====
    // Light passing through thin blade
    float3 H = normalize(L + N * _Translucency)  // Offset light through surface
    float VdotH = saturate(dot(V, -H))
    float translucent = pow(VdotH, 4.0) * _Translucency
    
    
    // ===== 5. AMBIENT OCCLUSION (HEIGHT-BASED) =====
    // Darker near base, lighter at tip
    float ao = lerp(0.6, 1.0, t)  // Base is 60% bright, tip is 100%
    
    
    // ===== 6. SPECULAR (OPTIONAL, ANISOTROPIC) =====
    // Grass has subtle anisotropic highlights along tangent
    float3 T = normalize(input.tangentWS)
    float TdotL = dot(T, L)
    float TdotV = dot(T, V)
    
    float anisotropic = sqrt(1.0 - TdotL * TdotL) * sqrt(1.0 - TdotV * TdotV) - TdotL * TdotV
    float specular = pow(saturate(anisotropic), 32.0) * 0.1  // Very subtle
    
    
    // ===== 7. COMBINE LIGHTING =====
    float3 ambient = baseColor * _AmbientStrength
    float3 diffuseLight = baseColor * diffuse * _LightColor
    float3 translucentLight = baseColor * translucent * _LightColor * 0.5
    float3 specularLight = float3(1, 1, 1) * specular
    
    float3 finalColor = (ambient + diffuseLight + translucentLight + specularLight) * ao
    
    
    // ===== 8. ALPHA (EDGE FADE) =====
    // Fade out edges to make blade look rounder
    float edgeFade = abs(input.uv.x * 2.0 - 1.0)  // 0 at center, 1 at edges
    float alpha = smoothstep(1.0, 0.7, edgeFade)  // Soft falloff
    
    // Also fade by distance (LOD)
    float distanceToCamera = length(_CameraPosition - input.positionWS)
    float distanceFade = smoothstep(100.0, 80.0, distanceToCamera)
    alpha *= distanceFade
    
    
    // ===== 9. FOG (OPTIONAL) =====
    finalColor = lerp(finalColor, _FogColor.rgb, input.fogFactor)
    
    
    // ===== 10. OUTPUT =====
    RETURN float4(finalColor, alpha)
```

---

## 8. COMPUTE SHADER (WIND TEXTURE GENERATION)

### Wind Update Compute Shader
```pseudocode
// This runs on GPU to update wind texture
// Called once per frame from CPU

#pragma kernel UpdateWind

RWEXTURE2D<float4> _WindTexture  // Output texture

// Parameters (set by CPU)
float _Time
float _WindScale
float _WindSpeed
float2 _WindDirection


FUNCTION Hash(float2 p) -> float:
    // Simple 2D hash for noise
    p = frac(p * float2(123.34, 345.56))
    p += dot(p, p + 34.45)
    RETURN frac(p.x * p.y)


FUNCTION Noise(float2 p) -> float:
    // Perlin-like noise
    float2 i = floor(p)
    float2 f = frac(p)
    
    float a = Hash(i)
    float b = Hash(i + float2(1, 0))
    float c = Hash(i + float2(0, 1))
    float d = Hash(i + float2(1, 1))
    
    float2 u = f * f * (3.0 - 2.0 * f)  // Smoothstep
    
    RETURN lerp(a, b, u.x) + (c - a) * u.y * (1.0 - u.x) + (d - b) * u.x * u.y


[numthreads(8, 8, 1)]
FUNCTION UpdateWind(uint3 id : SV_DispatchThreadID):
    // Get texture dimensions
    uint width, height
    _WindTexture.GetDimensions(width, height)
    
    // Convert to UV [0-1]
    float2 uv = (float2)id.xy / float2(width, height)
    
    // Time-based offset (wind flows)
    float2 offset = _WindDirection * _Time * _WindSpeed * 0.1
    
    // Sample multiple octaves of noise
    float2 samplePos = (uv + offset) * _WindScale
    
    float noise1 = Noise(samplePos * 1.0)
    float noise2 = Noise(samplePos * 2.0) * 0.5
    float noise3 = Noise(samplePos * 4.0) * 0.25
    
    float combinedNoise = noise1 + noise2 + noise3
    combinedNoise /= 1.75  // Normalize
    
    // Compute wind direction (swirls based on noise)
    float angle = combinedNoise * 6.28  // 0-2π
    float2 windDir = float2(sin(angle), cos(angle))
    
    // Wind strength
    float strength = combinedNoise
    
    // Pack into texture (R=dirX, G=dirZ, B=strength, A=unused)
    float4 windData = float4(
        windDir.x * 0.5 + 0.5,  // Remap [-1,1] to [0,1]
        windDir.y * 0.5 + 0.5,
        strength,
        1.0
    )
    
    _WindTexture[id.xy] = windData
```

---

## 9. ADVANCED FEATURES (OPTIONAL)

### Distance-Based LOD in Shader
```pseudocode
FUNCTION ApplyShaderLOD(VertexInput input, BladeInstanceData blade, float3 worldPos) -> bool:
    // Compute distance to camera
    float distance = length(_CameraPosition - blade.position)
    
    // Cull very far blades
    IF distance > 150.0:
        RETURN false  // Don't render this vertex
    
    // Reduce vertex displacement based on distance
    IF distance > 50.0:
        // Simplify wind (less expensive)
        // Could skip some calculations
    
    RETURN true
```

### Normal Map Support
```pseudocode
TEXTURE2D(_NormalMap)
SAMPLER(sampler_NormalMap)

FUNCTION ApplyNormalMap(VertexToFragment input) -> float3:
    // Sample normal map
    float3 normalTex = SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv).rgb
    normalTex = normalTex * 2.0 - 1.0  // Remap to [-1,1]
    
    // Build TBN matrix
    float3 N = normalize(input.normalWS)
    float3 T = normalize(input.tangentWS)
    float3 B = cross(N, T)
    
    float3x3 TBN = float3x3(T, B, N)
    
    // Transform normal from tangent space to world space
    float3 worldNormal = mul(normalTex, TBN)
    
    RETURN normalize(worldNormal)
```

### Color Variation Texture
```pseudocode
FUNCTION SampleColorVariation(float randomSeed) -> float3:
    // Use seed as UV into 1D gradient texture
    float2 uv = float2(randomSeed, 0.5)
    
    float3 variation = SAMPLE_TEXTURE2D(_ColorVariationTex, sampler_ColorVariationTex, uv).rgb
    
    RETURN variation
```

---

## 10. SHADER VARIANTS AND KEYWORDS

### Shader Features
```pseudocode
// Define shader variants for different quality levels

#pragma multi_compile _ USE_WIND
#pragma multi_compile _ USE_TRANSLUCENCY
#pragma multi_compile _ USE_NORMALMAP
#pragma multi_compile _ USE_ANISOTROPIC_SPECULAR


FUNCTION VertexShader_Optimized(VertexInput input) -> VertexToFragment:
    // ... base logic ...
    
    #ifdef USE_WIND
        worldPos += SampleWindDeformation(worldPos, blade, t)
    #endif
    
    // ... rest of shader ...


FUNCTION FragmentShader_Optimized(VertexToFragment input) -> float4:
    // ... base color ...
    
    #ifdef USE_NORMALMAP
        normal = ApplyNormalMap(input)
    #endif
    
    #ifdef USE_TRANSLUCENCY
        finalColor += ComputeTranslucency(input)
    #endif
    
    #ifdef USE_ANISOTROPIC_SPECULAR
        finalColor += ComputeAnisotropicSpecular(input)
    #endif
    
    RETURN float4(finalColor, alpha)
```

---

## 11. OPTIMIZATION TECHNIQUES

### GPU Performance Rules
```pseudocode
OPTIMIZE:
1. Minimize texture samples (reuse wind sample)
2. Avoid branching in fragment shader
3. Use mad (multiply-add) operations
4. Pre-compute constants in vertex shader
5. Use half precision where acceptable
6. Minimize varyings (VertexToFragment data)

AVOID:
1. Dynamic branching based on instance data
2. Texture sampling in loops
3. Complex math in fragment shader
4. Redundant normalize() calls
5. Unnecessary precision (use half for colors)
```

### Vertex Shader Optimizations
```pseudocode
// Pre-compute tangent vectors in constant buffer
CBUFFER ShapeProfiles:
    float3 precomputedTangents[8]  // One per shape profile

FUNCTION OptimizedVertexShader(VertexInput input) -> VertexToFragment:
    BladeInstanceData blade = BladeInstances[input.instanceID]
    
    // Use pre-computed tangent instead of computing in shader
    float3 m0 = precomputedTangents[blade.shapeProfileID]
    
    // ... rest of shader ...
```

### Fragment Shader Optimizations
```pseudocode
FUNCTION OptimizedFragmentShader(VertexToFragment input) -> half4:
    // Use half precision for colors (faster on mobile)
    half3 baseColor = lerp((half3)_GrassColorBase, (half3)_GrassColorTip, (half)input.uv.y)
    
    // Early exit for fully transparent pixels
    half alpha = ComputeAlpha(input)
    IF alpha < 0.01:
        discard  // Skip expensive lighting
    
    // ... lighting ...
    
    RETURN half4(finalColor, alpha)
```

---

## 12. DEBUGGING SHADERS

### Debug Visualizations
```pseudocode
// Add debug modes via shader keywords
#pragma multi_compile _ DEBUG_NORMALS DEBUG_TANGENTS DEBUG_UV DEBUG_INSTANCE

FUNCTION FragmentShader_Debug(VertexToFragment input) -> float4:
    
    #ifdef DEBUG_NORMALS
        // Visualize normals as RGB
        RETURN float4(input.normalWS * 0.5 + 0.5, 1)
    #endif
    
    #ifdef DEBUG_TANGENTS
        // Visualize tangents
        RETURN float4(input.tangentWS * 0.5 + 0.5, 1)
    #endif
    
    #ifdef DEBUG_UV
        // Visualize UV coordinates
        RETURN float4(input.uv.x, input.uv.y, 0, 1)
    #endif
    
    #ifdef DEBUG_INSTANCE
        // Color by instance ID
        float hue = frac((float)input.instanceID / 256.0)
        RETURN float4(HSVtoRGB(hue, 1, 1), 1)
    #endif
    
    // Normal rendering
    RETURN FragmentShader(input)
```

---

## 13. CRITICAL GPU RULES

### ✅ DO on GPU:
1. Evaluate Hermite/Bezier curves
2. Compute tangents and normals
3. Build blade geometry from parameters
4. Apply wind deformation
5. Sample textures (wind, density, color)
6. Lighting calculations
7. Alpha blending and edge fading

### ❌ DON'T on GPU:
1. Make LOD decisions (receive as input)
2. Allocate memory
3. Manage buffers
4. Decide which blades exist
5. Stream chunks
6. Heavy branching on per-blade data
7. Write to instance buffer (read-only)

---

## 14. SHADER STRUCTURE TEMPLATE

### Complete Shader File Structure
```pseudocode
Shader "Custom/GrassShader"
{
    Properties
    {
        _GrassColorBase ("Base Color", Color) = (0.2, 0.4, 0.1, 1)
        _GrassColorTip ("Tip Color", Color) = (0.4, 0.6, 0.2, 1)
        _WindStrength ("Wind Strength", Range(0, 2)) = 1
        _Translucency ("Translucency", Range(0, 1)) = 0.3
        
        // Textures
        _WindTexture ("Wind Texture", 2D) = "white" {}
        _ColorVariationTex ("Color Variation", 2D) = "white" {}
    }
    
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="AlphaTest" }
        
        Pass
        {
            Name "ForwardLit"
            
            Cull Off  // Two-sided rendering
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            // Feature toggles
            #pragma multi_compile _ USE_WIND
            #pragma multi_compile _ USE_TRANSLUCENCY
            
            // Include Unity helpers
            #include "UnityCG.cginc"
            
            // Structured buffers
            StructuredBuffer<BladeInstanceData> BladeInstances;
            
            // ... vertex shader ...
            // ... fragment shader ...
            
            ENDHLSL
        }
        
        // Shadow pass (simplified)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            
            ZWrite On
            ColorMask 0
            
            HLSLPROGRAM
            #pragma vertex vert_shadow
            #pragma fragment frag_shadow
            
            // Simplified shadow vertex shader (no wind for performance)
            
            ENDHLSL
        }
    }
}
```

---

## IMPLEMENTATION PRIORITY (GPU SIDE)

### Week 1: Basic Vertex Shader
1. Hermite curve functions
2. Basic blade construction (no wind)
3. Normal computation
4. Simple fragment shader (solid color)

### Week 2: Wind System
1. Wind texture generation (compute)
2. Wind sampling in vertex shader
3. Height-based attenuation
4. Per-blade phase variation

### Week 3: Lighting
1. Diffuse lighting (wrap)
2. Translucency/subsurface
3. Ambient occlusion (height-based)
4. Color gradients

### Week 4: Polish
1. Edge alpha fading
2. Anisotropic specular
3. Distance LOD
4. Shader variants

### Week 5: Optimization
1. Half precision
2. Minimize varyings
3. Pre-compute constants
4. Profile GPU performance
