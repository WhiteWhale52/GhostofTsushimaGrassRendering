# GPU GRASS SYSTEM - QUICK REFERENCE CARD

## THE GOLDEN RULES

### 1. THE CORE PRINCIPLE
**Blades are PARAMETERS, not GEOMETRY**
- CPU generates: position, height, width, curvature (64 bytes)
- GPU generates: 30 vertices from those parameters
- Never store final positions on CPU

### 2. THE CPU-GPU SPLIT
```
CPU:  WHAT exists (position, count, chunks)
GPU:  HOW it looks (curves, normals, lighting)
```

### 3. THE THREE NEVER-BREAK RULES
1. **NEVER** animate on CPU (use shaders)
2. **NEVER** rebuild meshes per frame (upload once)
3. **NEVER** use more vertices for smoothness (use better normals)

---

## CRITICAL CONCEPTS IN 30 SECONDS

### Parametric Curves
- Hermite curve = smooth path from p0 to p1 with tangents m0, m1
- Tangent = curve derivative = smooth normal direction
- 15 vertices on curved path > 30 vertices on straight path

### Instancing
- ONE mesh shared by ALL blades
- Each blade = instance with unique parameters
- SV_InstanceID tells GPU which blade's parameters to use

### BRG (BatchRendererGroup)
- One batch = one chunk = 5,000 blades = ONE draw call
- CPU culls batches (chunks), not individual blades
- GPU receives only visible batches

### Wind as Field
- Wind = 2D texture (direction + strength)
- Blades sample texture at their world position
- Updated once per frame, sampled per vertex

### Chunks
- World divided into 16×16m tiles
- Each chunk = one batch in BRG
- Stream in/out based on camera distance

---

## DO / DON'T QUICK LIST

### CPU
✅ Generate parameters (position, height, curvature)
✅ Use Jobs + Burst for parallel work
✅ Manage chunks and streaming
✅ Update wind texture once per frame
❌ Build final vertex positions
❌ Animate individual blades
❌ Touch geometry after GPU upload

### GPU (Vertex Shader)
✅ Evaluate Hermite curves
✅ Compute normals from derivatives
✅ Sample wind texture
✅ Build blade geometry
❌ Write to instance buffer
❌ Make spatial decisions

### GPU (Fragment Shader)
✅ Diffuse + translucency lighting
✅ Color variation
✅ Edge alpha fading
❌ Heavy branching
❌ Excessive texture samples

---

## DATA STRUCTURES

### BladeInstanceData (CPU → GPU)
```
struct (64 bytes total):
  - position (12 bytes): float3
  - facing (4 bytes): float
  - height (4 bytes): float
  - width (4 bytes): float
  - curvature (4 bytes): float
  - lean (4 bytes): float
  - shapeID (4 bytes): int
  - randomSeed (4 bytes): float
  - stiffness (4 bytes): float
  - windPhase (4 bytes): float
  - (padding to 64)
```

---

## HERMITE CURVE (THE MATH THAT MATTERS)

### Position
```
P(t) = h00(t)·p0 + h10(t)·m0 + h01(t)·p1 + h11(t)·m1

where:
  h00 = 2t³ - 3t² + 1
  h10 = t³ - 2t² + t
  h01 = -2t³ + 3t²
  h11 = t³ - t²
```

### Tangent (for normals)
```
T(t) = dP/dt = h'00·p0 + h'10·m0 + h'01·p1 + h'11·m1

where:
  h'00 = 6t² - 6t
  h'10 = 3t² - 4t + 1
  h'01 = -6t² + 6t
  h'11 = 3t² - 2t
```

### Frame Construction
```
T = normalize(tangent)
S = normalize(cross(worldUp, T))  // side
N = normalize(cross(T, S))         // normal
```

---

## VERTEX SHADER FLOW

```
1. Read blade = BladeInstances[instanceID]
2. Extract t = uv.y (height 0-1)
3. Build Hermite: p0, p1, m0, m1
4. Evaluate: center = P(t), tangent = T(t)
5. Build frame: (T, S, N)
6. Width taper: w = width × (1-t)^1.3
7. Offset: pos = center + S × side × w
8. Wind: pos += SampleWind(pos, blade, t)
9. Output: clip position, world normal
```

---

## CHUNK SYSTEM

### Coordinate Conversion
```
WorldToChunk: floor(worldPos / chunkSize)
ChunkToWorld: chunkCoord × chunkSize
```

### Chunk Lifecycle
```
Needed → Generate (Jobs) → Upload → Register (BRG) → Active → Remove → Pool
```

### Memory per Chunk
```
5,000 blades × 64 bytes = 320 KB
100 chunks = 32 MB (acceptable)
```

---

## BRG FLOW

```
1. CPU: Create chunks, upload parameters
2. CPU: AddBatch(mesh, material, instanceBuffer, bounds)
3. Unity: Calls OnPerformCulling
4. CPU: Mark batches visible/invisible
5. GPU: Renders visible batches only
```

---

## WIND SYSTEM

### Texture Format
```
RGBA:
  R = wind direction X (remapped to 0-1)
  G = wind direction Z (remapped to 0-1)
  B = wind strength (0-1)
  A = unused
```

### Sampling
```
uv = worldPos.xz × scale
wind = SampleTexture(windTex, uv)
dir = wind.rg × 2 - 1  // to [-1,1]
strength = wind.b
```

### Application
```
offset = dir × strength × sin(time + phase) × heightFactor × (1 - stiffness)
position += offset
```

---

## LIGHTING COMPONENTS

### Diffuse (Wrap)
```
ndl = dot(normal, light)
diffuse = saturate((ndl + 0.5) / 1.5)
```

### Translucency
```
H = normalize(light + normal × translucency)
trans = pow(saturate(dot(view, -H)), 4)
```

### AO (Height-based)
```
ao = lerp(0.6, 1.0, t)
```

### Edge Alpha
```
edge = abs(uv.x × 2 - 1)
alpha = smoothstep(1.0, 0.7, edge)
```

---

## PERFORMANCE BUDGETS

### Per Frame (CPU)
- Chunk updates: < 1 ms
- Job scheduling: < 0.5 ms
- Buffer uploads: < 0.5 ms
- BRG updates: < 0.5 ms
**Total CPU: < 2.5 ms**

### Per Frame (GPU)
- Vertex processing: < 5 ms
- Fragment shading: < 3 ms
- Wind texture update: < 0.5 ms
**Total GPU: < 8.5 ms**

### Memory
- Active chunks: < 100
- GPU buffers: < 50 MB
- Textures: < 10 MB
**Total: < 60 MB**

---

## COMMON MISTAKES TO AVOID

### ❌ Mistake 1: "More vertices = smoother"
**Reality:** Normals from curve derivatives = smooth with fewer vertices

### ❌ Mistake 2: "Each blade needs its own mesh"
**Reality:** All blades share ONE template mesh

### ❌ Mistake 3: "Animate on CPU"
**Reality:** Animation happens in shader

### ❌ Mistake 4: "Wind is per-blade state"
**Reality:** Wind is a texture field

### ❌ Mistake 5: "Rebuild mesh every frame"
**Reality:** Upload parameters once, shader does the rest

---

## DEBUG CHECKLIST

When something's wrong, check:

### Blades look faceted?
- ✓ Are normals computed from curve tangent?
- ✓ Are you using RecalculateNormals? (don't!)
- ✓ Is the Hermite tangent normalized?

### Blades all identical?
- ✓ Is randomSeed unique per blade?
- ✓ Is instanceID being used?
- ✓ Are parameters actually different in buffer?

### Performance poor?
- ✓ Are you rebuilding meshes per frame? (don't!)
- ✓ Too many chunks active?
- ✓ Is LOD working?
- ✓ Are batches being culled?

### Wind not working?
- ✓ Is wind texture being updated?
- ✓ Is it bound to shader?
- ✓ Are UVs calculated correctly?
- ✓ Is phase offset applied?

### Chunks not streaming?
- ✓ Is UpdateActiveChunks being called?
- ✓ Are coordinates calculated correctly?
- ✓ Are Jobs completing?
- ✓ Is buffer upload happening?

---

## IMPLEMENTATION ORDER

1. **Week 1:** Hermite curves, basic shader
2. **Week 2:** Instancing (100 blades)
3. **Week 3:** Chunks (streaming)
4. **Week 4:** BRG integration
5. **Week 5:** Jobs + Burst
6. **Week 6:** Wind system
7. **Week 7:** Lighting polish
8. **Week 8:** Optimization

---

## KEY FORMULAS

### Hermite Basis
```
h00(t) = 2t³ - 3t² + 1
h10(t) = t³ - 2t² + t
h01(t) = -2t³ + 3t²
h11(t) = t³ - t²
```

### Width Taper
```
w(t) = baseWidth × (1 - t)^1.3
```

### Wind Phase
```
phase = time × speed × freq + offset
sway = sin(phase)
```

### Height Attenuation
```
factor = t²
```

---

## MEMORY ALIGNMENT

### GPU Struct Alignment Rules
- float3 → 12 bytes (but aligned to 16)
- float4 → 16 bytes
- Structs aligned to 16 byte boundaries
- Keep total size multiple of 16 for best performance

### Example (GOOD):
```
struct BladeInstanceData {  // 64 bytes total
    float3 pos;      // 12 bytes
    float facing;    // 4 bytes (total 16 ✓)
    float height;    // 4 bytes
    float width;     // 4 bytes
    float curve;     // 4 bytes
    float lean;      // 4 bytes (total 32 ✓)
    // ... continues to 64
}
```

---

## SUCCESS METRICS

You're on track when:
- ✓ CPU time < 2.5 ms per frame
- ✓ GPU time < 8.5 ms per frame
- ✓ 60 FPS with 500,000 visible blades
- ✓ Memory usage < 60 MB
- ✓ Grass looks smooth at all distances
- ✓ Wind motion looks natural
- ✓ No allocations in Update

---

## WHEN STUCK, REMEMBER:

1. **Read the theory guide** - Understanding WHY > knowing HOW
2. **Start simple** - One blade before millions
3. **Test incrementally** - Each step should work before next
4. **Profile early** - Measure, don't guess
5. **Trust the separation** - CPU parameters, GPU geometry
6. **Use the examples** - Ghost of Tsushima did this right

**The system works. Follow the theory. Build incrementally. Profile constantly.**
