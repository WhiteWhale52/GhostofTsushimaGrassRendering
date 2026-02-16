## CORE DATA STRUCTURES

### 1. Blade Instance Data (Per-Blade Parameters)

```pseudocode
STRUCT BladeInstanceData:
    // Transform
    position: Vector3           // World position of blade root
    facingDirection: Vector2    // XZ direction blade faces (normalized)
    
    // Shape parameters
    height: float              // Total blade height
    width: float               // Base width
    curvatureStrength: float   // How much the blade bends
    tiltAngle: float          // Lean angle from vertical
    
    // Variation
    shapeProfileID: int       // Index into shape profiles (0-7)
    randomSeed: float         // Per-blade randomization [0-1]
    
    // Animation
    windPhaseOffset: float    // Offset for wind animation
    stiffness: float         // Resistance to wind [0-1]


STRUCT HermiteCurveParams:
    // Control points
    p0: Vector3               // Root position
    p1: Vector3               // Tip position
    
    // Tangent vectors (these control the curve shape)
    m0: Vector3               // Root tangent (direction blade grows from base)
    m1: Vector3               // Tip tangent (direction blade curves toward)
```

### 2. Chunk System Data

```pseudocode
STRUCT ChunkCoord:
    x: int                    // Grid X coordinate
    z: int                    // Grid Z coordinate
    
    FUNCTION GetHashCode():
        RETURN (x * 73856093) XOR (z * 19349663)
    
    FUNCTION Equals(other: ChunkCoord):
        RETURN x == other.x AND z == other.z


CLASS GrassChunk:
    // Identity
    coord: ChunkCoord
    worldOrigin: Vector3      // World position of chunk (0,0,0) corner
    bounds: Bounds            // AABB for culling
    
    // Rendering data
    mesh: Mesh                // The actual Unity mesh
    instanceDataBuffer: NativeArray<BladeInstanceData>
    
    // BRG data (will be used later)
    brgBatchID: int
    graphicsBuffer: GraphicsBuffer
    
    // State
    isVisible: bool
    currentLOD: int
    bladeCount: int
    
    // Generation parameters
    densityMap: float[]       // Optional: per-cell density [0-1]
    biomeType: int


CLASS GrassChunkManager:
    // Configuration
    chunkSize: float = 16.0           // World size of each chunk
    viewDistance: int = 3              // Chunks visible in each direction
    maxBladesPerChunk: int = 5000
    
    // Runtime data
    activeChunks: Dictionary<ChunkCoord, GrassChunk>
    chunkPool: Queue<GrassChunk>      // Reusable chunks
    
    // References
    cameraTransform: Transform
    templateBladeMesh: Mesh            // Shared mesh for all blades
    grassMaterial: Material
```

### 3. Blade Mesh Template (Shared by All Blades)

```pseudocode
STRUCT BladeVertex:
    position: Vector3         // Will be (0,0,0) initially - computed in shader
    normal: Vector3          // Computed from curve
    uv: Vector2              // .x = side (-1 or +1), .y = t [0-1]
    
    // Alternative minimal version:
    t: float                 // Parameter along blade height [0-1]
    side: float             // -1 (left edge) or +1 (right edge)


FUNCTION CreateTemplateBladeMesh(segmentCount: int):
    // We need 2 vertices per segment (left and right)
    vertexCount = (segmentCount + 1) * 2
    vertices = NEW ARRAY[vertexCount] of BladeVertex
    indices = NEW ARRAY[segmentCount * 6] of int  // 2 triangles per segment
    
    vertexIndex = 0
    
    FOR i FROM 0 TO segmentCount:
        t = i / segmentCount  // Height parameter [0 at base, 1 at tip]
        
        // Left vertex
        vertices[vertexIndex].position = Vector3(0, 0, 0)  // Computed in generation
        vertices[vertexIndex].uv = Vector2(-1, t)          // -1 = left side
        vertexIndex++
        
        // Right vertex  
        vertices[vertexIndex].position = Vector3(0, 0, 0)
        vertices[vertexIndex].uv = Vector2(+1, t)          // +1 = right side
        vertexIndex++
    
    // Build triangle indices
    indexIdx = 0
    FOR i FROM 0 TO segmentCount - 1:
        baseVert = i * 2
        
        // Triangle 1
        indices[indexIdx++] = baseVert + 0      // Current left
        indices[indexIdx++] = baseVert + 1      // Current right
        indices[indexIdx++] = baseVert + 2      // Next left
        
        // Triangle 2
        indices[indexIdx++] = baseVert + 1      // Current right
        indices[indexIdx++] = baseVert + 3      // Next right
        indices[indexIdx++] = baseVert + 2      // Next left
    
    mesh = NEW Mesh()
    mesh.vertices = vertices
    mesh.indices = indices
    mesh.indexFormat = UInt32
    
    RETURN mesh
```

---

## HERMITE CURVE MATHEMATICS (CPU Generation)

### Core Hermite Functions

```pseudocode
FUNCTION HermitePosition(p0: Vector3, p1: Vector3, m0: Vector3, m1: Vector3, t: float) -> Vector3:
    // Cubic Hermite interpolation
    t2 = t * t
    t3 = t2 * t
    
    h00 = 2*t3 - 3*t2 + 1      // Basis function for p0
    h10 = t3 - 2*t2 + t        // Basis function for m0
    h01 = -2*t3 + 3*t2         // Basis function for p1
    h11 = t3 - t2               // Basis function for m1
    
    RETURN h00*p0 + h10*m0 + h01*p1 + h11*m1


FUNCTION HermiteTangent(p0: Vector3, p1: Vector3, m0: Vector3, m1: Vector3, t: float) -> Vector3:
    // First derivative of Hermite curve
    t2 = t * t
    
    h00_prime = 6*t2 - 6*t
    h10_prime = 3*t2 - 4*t + 1
    h01_prime = -6*t2 + 6*t
    h11_prime = 3*t2 - 2*t
    
    RETURN h00_prime*p0 + h10_prime*m0 + h01_prime*p1 + h11_prime*m1


FUNCTION ComputeBladeFrame(tangent: Vector3, worldUp: Vector3) -> (Vector3, Vector3, Vector3):
    // Build orthonormal frame from tangent
    // Returns: (tangent, bitangent/side, normal)
    
    tangent = NORMALIZE(tangent)
    
    // Handle edge case: tangent nearly parallel to worldUp
    IF ABS(DOT(tangent, worldUp)) > 0.95:
        worldUp = Vector3(1, 0, 0)  // Fallback direction
    
    // Side direction (perpendicular to tangent, in horizontal-ish plane)
    side = NORMALIZE(CROSS(worldUp, tangent))
    
    // Normal (perpendicular to both tangent and side)
    normal = NORMALIZE(CROSS(tangent, side))
    
    RETURN (tangent, side, normal)
```

---

## BLADE GENERATION (CPU - PER CHUNK)

### Single Blade Generation

```pseudocode
FUNCTION GenerateSingleBlade(
    rootPosition: Vector3,
    height: float,
    width: float,
    curvature: float,
    facing: Vector2,
    segmentCount: int,
    OUTPUT vertices: List<Vector3>,
    OUTPUT normals: List<Vector3>,
    OUTPUT uvs: List<Vector2>,
    OUTPUT indices: List<int>
):
    // Setup Hermite curve control points
    p0 = rootPosition
    p1 = rootPosition + Vector3(0, height, 0)  // Tip directly above (will be curved)
    
    // Root tangent: grows upward with slight forward lean
    facingDir3D = Vector3(facing.x, 0, facing.y)
    m0 = NORMALIZE(Vector3(0, 1, 0) + facingDir3D * 0.2) * height * 0.5
    
    // Tip tangent: curves based on curvature parameter
    curveDirection = facingDir3D * curvature
    m1 = NORMALIZE(Vector3(0, 1, 0) + curveDirection) * height * 0.3
    
    baseVertexIndex = vertices.Count
    
    // Generate vertices along the curve
    FOR i FROM 0 TO segmentCount:
        t = i / segmentCount
        
        // Evaluate curve at this height
        centerPoint = HermitePosition(p0, p1, m0, m1, t)
        tangent = HermiteTangent(p0, p1, m0, m1, t)
        
        // Build local frame
        (tangentNorm, sideDir, normalDir) = ComputeBladeFrame(tangent, Vector3(0,1,0))
        
        // Width taper (wider at base, narrow at tip)
        currentWidth = width * POW(1.0 - t, 1.3)  // Nonlinear taper
        
        // Generate left and right vertices
        leftPos = centerPoint - sideDir * currentWidth
        rightPos = centerPoint + sideDir * currentWidth
        
        // Add vertices (BOTH use same normal for smooth shading)
        vertices.Add(leftPos)
        normals.Add(normalDir)
        uvs.Add(Vector2(0, t))  // Left side
        
        vertices.Add(rightPos)
        normals.Add(normalDir)
        uvs.Add(Vector2(1, t))  // Right side
    
    // Generate triangle indices for this blade
    FOR i FROM 0 TO segmentCount - 1:
        baseVert = baseVertexIndex + i * 2
        
        // Triangle 1
        indices.Add(baseVert + 0)
        indices.Add(baseVert + 1)
        indices.Add(baseVert + 2)
        
        // Triangle 2
        indices.Add(baseVert + 1)
        indices.Add(baseVert + 3)
        indices.Add(baseVert + 2)
```

### Chunk-Level Blade Generation

```pseudocode
FUNCTION GenerateChunkBlades(chunk: GrassChunk, bladeCount: int, segmentCount: int) -> Mesh:
    vertices = NEW List<Vector3>()
    normals = NEW List<Vector3>()
    uvs = NEW List<Vector2>()
    indices = NEW List<int>()
    
    random = NEW Random(chunk.coord.GetHashCode())  // Deterministic per chunk
    
    FOR i FROM 0 TO bladeCount - 1:
        // Random position within chunk
        localX = random.NextFloat(0, chunkSize)
        localZ = random.NextFloat(0, chunkSize)
        worldPos = chunk.worldOrigin + Vector3(localX, 0, localZ)
        
        // Sample height from terrain (if available)
        terrainHeight = SampleTerrainHeight(worldPos)
        worldPos.y = terrainHeight
        
        // Optional: Sample density map to reject some blades
        densityValue = SampleDensityMap(chunk, localX, localZ)
        IF random.NextFloat() > densityValue:
            CONTINUE  // Skip this blade
        
        // Randomize blade parameters
        height = RandomRange(0.8, 1.5) * baseBladeHeight
        width = RandomRange(0.02, 0.04)
        curvature = RandomRange(-0.3, 0.3)
        facing = RandomInsideUnitCircle().Normalized()
        
        // Generate this blade's geometry
        GenerateSingleBlade(
            worldPos, height, width, curvature, facing, 
            segmentCount,
            vertices, normals, uvs, indices
        )
    
    // Create Unity mesh
    mesh = NEW Mesh()
    mesh.indexFormat = IndexFormat.UInt32  // Important for large meshes
    mesh.SetVertices(vertices)
    mesh.SetNormals(normals)  // DO NOT call RecalculateNormals()
    mesh.SetUVs(0, uvs)
    mesh.SetTriangles(indices, 0)
    mesh.RecalculateBounds()
    
    RETURN mesh
```

---

## CHUNK MANAGEMENT SYSTEM

### Coordinate Conversion

```pseudocode
FUNCTION WorldToChunkCoord(worldPos: Vector3, chunkSize: float) -> ChunkCoord:
    chunkX = FLOOR(worldPos.x / chunkSize)
    chunkZ = FLOOR(worldPos.z / chunkSize)
    RETURN ChunkCoord(chunkX, chunkZ)


FUNCTION ChunkCoordToWorldOrigin(coord: ChunkCoord, chunkSize: float) -> Vector3:
    RETURN Vector3(
        coord.x * chunkSize,
        0,
        coord.z * chunkSize
    )
```

### Chunk Manager Core Logic

```pseudocode
CLASS GrassChunkManager:
    
    FUNCTION Initialize():
        // Create shared template mesh (used by all grass later with BRG)
        templateBladeMesh = CreateTemplateBladeMesh(segmentCount: 15)
        
        // Pre-create chunk pool for reuse
        FOR i FROM 0 TO 50:
            chunk = NEW GrassChunk()
            chunkPool.Enqueue(chunk)
    
    
    FUNCTION Update():
        // Get camera chunk position
        cameraChunk = WorldToChunkCoord(cameraTransform.position, chunkSize)
        
        // Determine which chunks should be active
        requiredChunks = NEW Set<ChunkCoord>()
        
        FOR dz FROM -viewDistance TO viewDistance:
            FOR dx FROM -viewDistance TO viewDistance:
                coord = ChunkCoord(
                    cameraChunk.x + dx,
                    cameraChunk.z + dz
                )
                requiredChunks.Add(coord)
        
        // Create new chunks that are needed
        FOR EACH coord IN requiredChunks:
            IF NOT activeChunks.ContainsKey(coord):
                CreateOrActivateChunk(coord)
        
        // Deactivate chunks that are too far
        chunksToRemove = NEW List<ChunkCoord>()
        
        FOR EACH (coord, chunk) IN activeChunks:
            IF NOT requiredChunks.Contains(coord):
                chunksToRemove.Add(coord)
        
        FOR EACH coord IN chunksToRemove:
            DeactivateChunk(coord)
    
    
    FUNCTION CreateOrActivateChunk(coord: ChunkCoord):
        // Try to reuse from pool
        chunk = IF chunkPool.Count > 0 THEN chunkPool.Dequeue() ELSE NEW GrassChunk()
        
        chunk.coord = coord
        chunk.worldOrigin = ChunkCoordToWorldOrigin(coord, chunkSize)
        
        // Generate the mesh (CPU for now)
        chunk.mesh = GenerateChunkBlades(
            chunk,
            bladeCount: maxBladesPerChunk,
            segmentCount: 15
        )
        
        // Calculate bounds
        chunk.bounds = NEW Bounds(
            chunk.worldOrigin + Vector3(chunkSize/2, 0, chunkSize/2),
            Vector3(chunkSize, 10, chunkSize)  // Assume max grass height ~10
        )
        
        // Create GameObject with MeshRenderer (for now - will replace with BRG later)
        chunk.gameObject = NEW GameObject("Chunk_" + coord.x + "_" + coord.z)
        meshFilter = chunk.gameObject.AddComponent<MeshFilter>()
        meshRenderer = chunk.gameObject.AddComponent<MeshRenderer>()
        
        meshFilter.mesh = chunk.mesh
        meshRenderer.material = grassMaterial
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off  // Performance
        
        chunk.isVisible = true
        activeChunks[coord] = chunk
    
    
    FUNCTION DeactivateChunk(coord: ChunkCoord):
        chunk = activeChunks[coord]
        
        // Hide/destroy GameObject
        chunk.gameObject.SetActive(false)
        // OR: Destroy(chunk.gameObject)
        
        // Optionally destroy mesh to free memory
        // Destroy(chunk.mesh)
        
        // Return to pool for reuse
        chunk.isVisible = false
        chunkPool.Enqueue(chunk)
        
        activeChunks.Remove(coord)
```

---

## JOBS + BURST INTEGRATION (Phase 3)

### Blade Generation Job

```pseudocode
[BurstCompile]
STRUCT GenerateBladesJob : IJobParallelFor:
    // Input (read-only)
    [ReadOnly] chunkOrigin: Vector3
    [ReadOnly] chunkSize: float
    [ReadOnly] seed: int
    [ReadOnly] segmentCount: int
    [ReadOnly] bladesPerChunk: int
    
    // Output (write)
    [WriteOnly] bladeInstanceData: NativeArray<BladeInstanceData>
    
    FUNCTION Execute(index: int):
        // Each index = one blade
        random = NEW Random(seed + index)
        
        // Random position within chunk
        localX = random.NextFloat(0, chunkSize)
        localZ = random.NextFloat(0, chunkSize)
        position = chunkOrigin + Vector3(localX, 0, localZ)
        
        // Create blade instance data
        blade = NEW BladeInstanceData()
        blade.position = position
        blade.height = RandomRange(random, 0.8, 1.5)
        blade.width = RandomRange(random, 0.02, 0.04)
        blade.curvatureStrength = RandomRange(random, -0.3, 0.3)
        blade.facingDirection = RandomDirection2D(random)
        blade.shapeProfileID = random.NextInt(0, 8)
        blade.randomSeed = random.NextFloat()
        blade.windPhaseOffset = random.NextFloat(0, TWO_PI)
        blade.stiffness = RandomRange(random, 0.3, 0.9)
        
        bladeInstanceData[index] = blade


FUNCTION ScheduleBladeGeneration(chunk: GrassChunk):
    // Allocate native array for job output
    bladeData = NEW NativeArray<BladeInstanceData>(
        maxBladesPerChunk,
        Allocator.TempJob
    )
    
    // Create and schedule job
    job = NEW GenerateBladesJob {
        chunkOrigin = chunk.worldOrigin,
        chunkSize = chunkSize,
        seed = chunk.coord.GetHashCode(),
        segmentCount = 15,
        bladesPerChunk = maxBladesPerChunk,
        bladeInstanceData = bladeData
    }
    
    jobHandle = job.Schedule(
        arrayLength: maxBladesPerChunk,
        innerloopBatchCount: 64  // Process 64 blades per thread
    )
    
    // Store handle to complete later
    chunk.pendingJobHandle = jobHandle
    chunk.pendingBladeData = bladeData


FUNCTION CompleteBladeGeneration(chunk: GrassChunk):
    // Wait for job to finish
    chunk.pendingJobHandle.Complete()
    
    // Now convert bladeInstanceData to actual mesh
    // (This part still needs to be on main thread for Unity API)
    mesh = BuildMeshFromBladeData(
        chunk.pendingBladeData,
        segmentCount: 15
    )
    
    chunk.mesh = mesh
    
    // Clean up native array
    chunk.pendingBladeData.Dispose()
```

### Mesh Building from Instance Data

```pseudocode
FUNCTION BuildMeshFromBladeData(
    bladeData: NativeArray<BladeInstanceData>,
    segmentCount: int
) -> Mesh:
    
    verticesPerBlade = (segmentCount + 1) * 2
    trianglesPerBlade = segmentCount * 2
    
    totalVertices = bladeData.Length * verticesPerBlade
    totalIndices = bladeData.Length * trianglesPerBlade * 3
    
    vertices = NEW NativeArray<Vector3>(totalVertices, Allocator.Temp)
    normals = NEW NativeArray<Vector3>(totalVertices, Allocator.Temp)
    uvs = NEW NativeArray<Vector2>(totalVertices, Allocator.Temp)
    indices = NEW NativeArray<int>(totalIndices, Allocator.Temp)
    
    FOR bladeIndex FROM 0 TO bladeData.Length - 1:
        blade = bladeData[bladeIndex]
        
        // Setup Hermite curve for this blade
        p0 = blade.position
        p1 = blade.position + Vector3(0, blade.height, 0)
        
        facing3D = Vector3(blade.facingDirection.x, 0, blade.facingDirection.y)
        m0 = NORMALIZE(Vector3(0,1,0) + facing3D * 0.2) * blade.height * 0.5
        m1 = NORMALIZE(Vector3(0,1,0) + facing3D * blade.curvatureStrength) * blade.height * 0.3
        
        baseVertIdx = bladeIndex * verticesPerBlade
        
        // Generate vertices for this blade
        FOR segment FROM 0 TO segmentCount:
            t = segment / segmentCount
            
            center = HermitePosition(p0, p1, m0, m1, t)
            tangent = HermiteTangent(p0, p1, m0, m1, t)
            (tangentNorm, sideDir, normal) = ComputeBladeFrame(tangent, Vector3(0,1,0))
            
            currentWidth = blade.width * POW(1.0 - t, 1.3)
            
            vertIdx = baseVertIdx + segment * 2
            
            // Left vertex
            vertices[vertIdx] = center - sideDir * currentWidth
            normals[vertIdx] = normal
            uvs[vertIdx] = Vector2(0, t)
            
            // Right vertex
            vertices[vertIdx + 1] = center + sideDir * currentWidth
            normals[vertIdx + 1] = normal
            uvs[vertIdx + 1] = Vector2(1, t)
        
        // Generate indices for this blade
        baseIdxIdx = bladeIndex * trianglesPerBlade * 3
        
        FOR segment FROM 0 TO segmentCount - 1:
            baseVert = baseVertIdx + segment * 2
            idxIdx = baseIdxIdx + segment * 6
            
            indices[idxIdx + 0] = baseVert + 0
            indices[idxIdx + 1] = baseVert + 1
            indices[idxIdx + 2] = baseVert + 2
            
            indices[idxIdx + 3] = baseVert + 1
            indices[idxIdx + 4] = baseVert + 3
            indices[idxIdx + 5] = baseVert + 2
    
    // Create Unity mesh
    mesh = NEW Mesh()
    mesh.indexFormat = IndexFormat.UInt32
    mesh.SetVertices(vertices)
    mesh.SetNormals(normals)
    mesh.SetUVs(0, uvs)
    mesh.SetTriangles(indices, 0)
    mesh.RecalculateBounds()
    
    // Dispose native arrays
    vertices.Dispose()
    normals.Dispose()
    uvs.Dispose()
    indices.Dispose()
    
    RETURN mesh
```

---

## BRG (BATCH RENDERER GROUP) INTEGRATION (Phase 4)

### BRG Setup

```pseudocode
CLASS GrassChunkManager_BRG:
    batchRendererGroup: BatchRendererGroup
    sharedBladeMesh: Mesh
    grassMaterial: Material
    
    FUNCTION Initialize():
        // Create BRG instance
        batchRendererGroup = NEW BatchRendererGroup(OnPerformCulling)
        
        // Create minimal shared mesh (will be instanced many times)
        sharedBladeMesh = CreateTemplateBladeMesh(segmentCount: 15)
    
    
    FUNCTION OnPerformCulling(cullingContext: BatchCullingContext) -> JobHandle:
        // This callback lets you control visibility per-batch
        // For now, just mark all batches visible
        // Later: implement frustum culling, distance culling, etc.
        
        RETURN default(JobHandle)
    
    
    FUNCTION CreateChunkWithBRG(coord: ChunkCoord):
        chunk = NEW GrassChunk()
        chunk.coord = coord
        chunk.worldOrigin = ChunkCoordToWorldOrigin(coord, chunkSize)
        
        // Generate blade instance data using Jobs
        bladeData = GenerateBladeInstanceDataWithJobs(chunk)
        
        // Create GraphicsBuffer for instance data
        instanceBuffer = NEW GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            bladeData.Length,
            SizeOf<BladeInstanceData>()
        )
        
        // Upload instance data to GPU
        instanceBuffer.SetData(bladeData)
        
        // Register batch with BRG
        metadata = NEW NativeArray<MetadataValue>(/* BRG metadata */)
        
        batchID = batchRendererGroup.AddBatch(
            sharedBladeMesh,
            subMeshIndex: 0,
            grassMaterial,
            chunk.bounds,
            instanceCount: bladeData.Length,
            customProps: metadata,
            gameObjectId: 0
        )
        
        chunk.brgBatchID = batchID
        chunk.graphicsBuffer = instanceBuffer
        chunk.bladeCount = bladeData.Length
        
        activeChunks[coord] = chunk
```

### Shader Integration (What BRG Needs)

```pseudocode
// VERTEX SHADER PSEUDOCODE (HLSL-like)

STRUCT VertexInput:
    position: float3       // From template mesh (will be mostly zero)
    normal: float3         // From template mesh
    uv: float2            // .x = side, .y = t
    instanceID: uint      // Automatic from BRG


STRUCT VertexOutput:
    position: float4      // Clip space position
    worldPos: float3
    normal: float3
    uv: float2


// Instance data (matches BladeInstanceData struct)
STRUCTURED_BUFFER(BladeInstances) : BladeInstanceData[]


FUNCTION VertexShader(input: VertexInput) -> VertexOutput:
    // Fetch instance data for this blade
    blade = BladeInstances[input.instanceID]
    
    // Extract parameters from UV
    t = input.uv.y           // Height parameter [0-1]
    side = input.uv.x        // -1 (left) or +1 (right)
    
    // Setup Hermite curve
    p0 = blade.position
    p1 = blade.position + float3(0, blade.height, 0)
    
    facing3D = float3(blade.facingDirection.x, 0, blade.facingDirection.y)
    m0 = normalize(float3(0,1,0) + facing3D * 0.2) * blade.height * 0.5
    m1 = normalize(float3(0,1,0) + facing3D * blade.curvatureStrength) * blade.height * 0.3
    
    // Evaluate curve at t
    center = HermitePosition(p0, p1, m0, m1, t)
    tangent = HermiteTangent(p0, p1, m0, m1, t)
    
    // Build frame
    (tangentNorm, sideDir, normalDir) = ComputeBladeFrame(tangent, float3(0,1,0))
    
    // Width taper
    width = blade.width * pow(1.0 - t, 1.3)
    
    // Final world position
    worldPos = center + sideDir * (side * width)
    
    // Wind deformation (shader-only animation)
    windOffset = SampleWindTexture(worldPos.xz, time)
    windStrength = blade.stiffness * t * t  // More movement at tip
    worldPos += windOffset * windStrength
    
    // Output
    output = NEW VertexOutput()
    output.worldPos = worldPos
    output.position = WorldToClipPos(worldPos)
    output.normal = normalDir
    output.uv = input.uv
    
    RETURN output
```

---

## DENSITY MAP STREAMING (Phase 6)

### Density Map Data Structure

```pseudocode
CLASS DensityMapManager:
    densityTextureArray: Texture2DArray  // One slice per chunk
    densityResolution: int = 64          // 64x64 grid per chunk
    
    activeDensitySlices: Dictionary<ChunkCoord, int>  // Maps chunk -> texture slice
    availableSlices: Queue<int>          // Pool of unused slices
    
    FUNCTION Initialize(maxChunks: int):
        // Create texture array
        densityTextureArray = NEW Texture2DArray(
            width: densityResolution,
            height: densityResolution,
            depth: maxChunks,
            format: TextureFormat.R8,  // Single channel, 0-255
            mipChain: false
        )
        
        // Initialize available slice pool
        FOR i FROM 0 TO maxChunks - 1:
            availableSlices.Enqueue(i)
    
    
    FUNCTION GenerateDensityForChunk(coord: ChunkCoord) -> NativeArray<byte>:
        densityData = NEW NativeArray<byte>(
            densityResolution * densityResolution,
            Allocator.Temp
        )
        
        // Generate density using noise or other logic
        FOR y FROM 0 TO densityResolution - 1:
            FOR x FROM 0 TO densityResolution - 1:
                worldX = coord.x * chunkSize + (x / densityResolution) * chunkSize
                worldZ = coord.z * chunkSize + (y / densityResolution) * chunkSize
                
                // Sample noise or biome data
                noiseValue = PerlinNoise(worldX * 0.1, worldZ * 0.1)
                density = CLAMP(noiseValue, 0, 1)
                
                index = y * densityResolution + x
                densityData[index] = (byte)(density * 255)
        
        RETURN densityData
    
    
    FUNCTION UploadDensityForChunk(coord: ChunkCoord):
        // Get available slice
        IF availableSlices.Count == 0:
            RETURN  // No space - need to evict old chunk
        
        sliceIndex = availableSlices.Dequeue()
        
        // Generate density data (can be done in Jobs)
        densityData = GenerateDensityForChunk(coord)
        
        // Upload to GPU texture array
        densityTextureArray.SetPixelData(
            densityData,
            mipLevel: 0,
            sourceDataStartIndex: 0,
            sliceIndex: sliceIndex
        )
        densityTextureArray.Apply(updateMipmaps: false)
        
        // Track mapping
        activeDensitySlices[coord] = sliceIndex
        
        densityData.Dispose()
    
    
    FUNCTION SampleDensity(chunk: GrassChunk, localX: float, localZ: float) -> float:
        IF NOT activeDensitySlices.ContainsKey(chunk.coord):
            RETURN 1.0  // Default: full density
        
        sliceIndex = activeDensitySlices[chunk.coord]
        
        // Convert local position to UV coordinates
        u = localX / chunkSize
        v = localZ / chunkSize
        
        // Sample texture (on CPU - for blade placement)
        // In shader, you'd use texture array sampling
        pixelX = (int)(u * densityResolution)
        pixelY = (int)(v * densityResolution)
        
        pixel = densityTextureArray.GetPixel(pixelX, pixelY, sliceIndex)
        
        RETURN pixel.r  // Return red channel [0-1]
```

---

## WIND SYSTEM (Phase 7 - Shader Only)

### Wind Texture Generation

```pseudocode
CLASS WindSystem:
    windTexture: RenderTexture
    windResolution: int = 512
    windScale: float = 0.05
    windSpeed: float = 1.0
    
    FUNCTION Initialize():
        // Create wind noise texture
        windTexture = NEW RenderTexture(
            windResolution,
            windResolution,
            depth: 0,
            format: RenderTextureFormat.ARGBFloat
        )
        windTexture.wrapMode = TextureWrapMode.Repeat
    
    
    FUNCTION UpdateWindTexture(time: float):
        // Update wind using compute shader or material blit
        // Generate flowing Perlin noise that moves over time
        
        FOR y FROM 0 TO windResolution - 1:
            FOR x FROM 0 TO windResolution - 1:
                u = x / windResolution
                v = y / windResolution
                
                // Layered noise for wind
                offset = time * windSpeed
                
                noise1 = PerlinNoise((u + offset) * windScale, v * windScale)
                noise2 = PerlinNoise(u * windScale * 2, (v + offset * 0.5) * windScale * 2) * 0.5
                
                windStrength = noise1 + noise2
                
                // Wind direction (could be global + local variation)
                windDirX = COS(windStrength * TWO_PI)
                windDirZ = SIN(windStrength * TWO_PI)
                
                // Store in texture (R=dirX, G=dirZ, B=strength)
                windTexture.SetPixel(x, y, Color(windDirX, windDirZ, windStrength, 1))
        
        windTexture.Apply()


// SHADER INTEGRATION (HLSL-like pseudocode)

TEXTURE2D(WindTexture)
SAMPLER(sampler_WindTexture)

FUNCTION SampleWind(worldPos: float3, time: float) -> float3:
    // Sample wind texture using world position
    uv = worldPos.xz * 0.01  // Scale to texture space
    
    windData = SAMPLE_TEXTURE2D(WindTexture, sampler_WindTexture, uv)
    
    windDirection = float3(windData.r, 0, windData.g) * 2.0 - 1.0  // Remap [-1,1]
    windStrength = windData.b
    
    // Add time-based variation
    windOffset = windDirection * windStrength * sin(time + worldPos.x * 0.1)
    
    RETURN windOffset


// In vertex shader:
FUNCTION ApplyWind(worldPos: float3, t: float, stiffness: float, phaseOffset: float) -> float3:
    windOffset = SampleWind(worldPos, time + phaseOffset)
    
    // More wind at blade tip (t = 1), less at base (t = 0)
    heightFactor = t * t
    
    // Stiffness reduces wind effect
    windFactor = heightFactor * (1.0 - stiffness) * 0.5
    
    RETURN worldPos + windOffset * windFactor
```

---

## LOD SYSTEM (Phase 8)

### LOD Configuration

```pseudocode
ENUM GrassLOD:
    High = 0      // 15 segments, full detail
    Medium = 1    // 7 segments
    Low = 2       // 3 segments
    Billboard = 3 // Single quad, camera-facing


CLASS LODManager:
    lodDistances: float[] = [20, 50, 100]  // Distance thresholds
    
    FUNCTION ComputeLOD(distanceToCamera: float) -> GrassLOD:
        IF distanceToCamera < lodDistances[0]:
            RETURN GrassLOD.High
        ELSE IF distanceToCamera < lodDistances[1]:
            RETURN GrassLOD.Medium
        ELSE IF distanceToCamera < lodDistances[2]:
            RETURN GrassLOD.Low
        ELSE:
            RETURN GrassLOD.Billboard
    
    
    FUNCTION UpdateChunkLOD(chunk: GrassChunk, cameraPos: Vector3):
        chunkCenter = chunk.worldOrigin + Vector3(chunkSize/2, 0, chunkSize/2)
        distance = DISTANCE(chunkCenter, cameraPos)
        
        newLOD = ComputeLOD(distance)
        
        IF newLOD != chunk.currentLOD:
            // Rebuild mesh with different segment count
            segmentCount = MATCH newLOD:
                High -> 15
                Medium -> 7
                Low -> 3
                Billboard -> 1
            
            // Regenerate mesh (or swap to pre-built LOD mesh)
            chunk.mesh = GenerateChunkBlades(chunk, chunk.bladeCount, segmentCount)
            chunk.currentLOD = newLOD
```

---

## MAIN EXECUTION FLOW

### Initialization

```pseudocode
FUNCTION OnStart():
    // 1. Initialize managers
    chunkManager = NEW GrassChunkManager()
    chunkManager.Initialize()
    
    densityManager = NEW DensityMapManager()
    densityManager.Initialize(maxChunks: 100)
    
    windSystem = NEW WindSystem()
    windSystem.Initialize()
    
    // 2. Set up material with wind texture
    grassMaterial.SetTexture("_WindTexture", windSystem.windTexture)


FUNCTION OnUpdate():
    // 1. Update wind (if not using compute shader)
    windSystem.UpdateWindTexture(Time.time)
    
    // 2. Update chunk visibility
    chunkManager.Update()
    
    // 3. Update LODs for active chunks
    FOR EACH (coord, chunk) IN chunkManager.activeChunks:
        LODManager.UpdateChunkLOD(chunk, camera.position)
```

### Frame Execution Order

```pseudocode
FRAME EXECUTION:
    1. Jobs scheduled (blade generation for new chunks)
    2. Jobs complete
    3. Meshes built from job results
    4. BRG batches updated (instance buffers)
    5. Culling callback executed (per-batch visibility)
    6. GPU renders visible batches
        a. Vertex shader evaluates curves
        b. Wind applied
        c. Fragment shader lights grass
```

---

## OPTIMIZATION CHECKLIST

### Memory Management

```pseudocode
AVOID:
- Creating new List/Array each frame
- String concatenation in Update
- LINQ queries
- Repeated GetComponent calls

USE:
- Object pooling for chunks
- NativeArray with Allocator.TempJob
- StringBuilder for debug text
- Cached component references
- Static buffers where possible
```

### Performance Tips

```pseudocode
RULES:
1. NEVER rebuild meshes every frame
2. NEVER animate grass on CPU
3. NEVER use RecalculateNormals() - compute analytically
4. NEVER use individual GameObjects per blade
5. NEVER update transforms - use shaders

6. DO reuse chunk meshes when streaming
7. DO use Jobs + Burst for generation
8. DO use BRG for rendering
9. DO disable shadows on grass
10. DO profile frequently (Unity Profiler)
```

---

## DEBUGGING HELPERS

### Visualization

```pseudocode
FUNCTION DrawChunkBounds():
    FOR EACH chunk IN activeChunks:
        Gizmos.DrawWireCube(chunk.bounds.center, chunk.bounds.size)


FUNCTION DrawBladeNormals(chunk: GrassChunk):
    FOR i FROM 0 TO chunk.mesh.vertexCount - 1 STEP 2:
        pos = chunk.mesh.vertices[i]
        normal = chunk.mesh.normals[i]
        
        Debug.DrawRay(pos, normal * 0.1, Color.blue)


FUNCTION ShowChunkInfo():
    text = "Active Chunks: " + activeChunks.Count
    text += "\nTotal Blades: " + (activeChunks.Count * maxBladesPerChunk)
    text += "\nTotal Vertices: " + (activeChunks.Count * maxBladesPerChunk * 30)
    
    Debug.Log(text)
```

---

## CRITICAL IMPLEMENTATION ORDER

```
WEEK 1: Core Foundation
├── Day 1: BladeInstanceData + ChunkCoord structs
├── Day 2: Hermite curve functions
├── Day 3: Single blade generation
├── Day 4: Template mesh creation
└── Day 5: Test 100 static blades

WEEK 2: Chunk System
├── Day 1: GrassChunk class
├── Day 2: Coordinate conversion
├── Day 3: Chunk generation
├── Day 4: ChunkManager Update() logic
└── Day 5: Test chunk streaming

WEEK 3: Jobs + Burst
├── Day 1: Setup Jobs package
├── Day 2: GenerateBladesJob struct
├── Day 3: Job scheduling
├── Day 4: Mesh building from job data
└── Day 5: Profile and optimize

WEEK 4: BRG
├── Day 1: Setup BRG
├── Day 2: GraphicsBuffer creation
├── Day 3: Batch registration
├── Day 4: Culling callback
└── Day 5: Test BRG vs MeshRenderer

WEEK 5: Shader
├── Day 1: Vertex shader setup
├── Day 2: Hermite evaluation in shader
├── Day 3: Normal computation
├── Day 4: Fragment shader
└── Day 5: Material properties

WEEK 6: Density
├── Day 1: Texture2DArray setup
├── Day 2: Density generation
├── Day 3: Upload pipeline
├── Day 4: Blade placement integration
└── Day 5: Test density variation

WEEK 7: Wind
├── Day 1: Wind texture generation
├── Day 2: Shader wind sampling
├── Day 3: Per-blade phase
├── Day 4: Height attenuation
└── Day 5: Tune parameters

WEEK 8: LOD
├── Day 1: LOD distance setup
├── Day 2: Per-chunk LOD
├── Day 3: Mesh swapping
├── Day 4: Billboard LOD
└── Day 5: Smooth transitions

WEEK 9: Polish
├── Day 1: Alpha edge falloff
├── Day 2: Normal biasing
├── Day 3: Color variation
├── Day 4: Performance profiling
└── Day 5: Bug fixes

WEEK 10: Final
├── Test at scale
├── Optimize bottlenecks
├── Documentation
└── Celebrate 🌱
```