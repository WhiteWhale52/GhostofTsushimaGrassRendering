# CPU-SIDE IMPLEMENTATION GUIDE (C# Unity Code)
## What Belongs on the CPU and Why

---

## PHILOSOPHY: CPU Responsibilities

The CPU should ONLY handle:
- **Spatial management** (chunks, streaming, culling)
- **Data generation** (blade parameters, not geometry)
- **Memory orchestration** (buffers, uploads)
- **High-level decisions** (LOD, visibility, what exists)

The CPU should NEVER:
- ❌ Build final vertex positions
- ❌ Compute curve positions every frame
- ❌ Animate grass
- ❌ Update transforms
- ❌ Touch individual blades

---

## 1. CORE DATA STRUCTURES (CPU Memory)

### Blade Instance Parameters (What CPU Creates)
```pseudocode
// This is what you GENERATE on CPU and SEND to GPU
// NOT the actual geometry - just parameters

STRUCT BladeInstanceData:
    // 64 bytes total (GPU-friendly alignment)
    
    // Transform (16 bytes)
    positionX: float
    positionY: float  
    positionZ: float
    facingAngle: float        // Single angle, not full direction
    
    // Shape (16 bytes)
    height: float
    width: float
    curvature: float         // -1 to 1, how much it bends
    lean: float             // Tilt from vertical
    
    // Variation (16 bytes)
    shapeProfileID: int     // 0-255, index into shape table
    colorVariationSeed: float
    randomSeed: float
    stiffness: float        // Wind resistance
    
    // Animation (16 bytes)
    windPhaseOffset: float
    swayFrequency: float
    padding1: float         // Keep 16-byte alignment
    padding2: float

// NOTE: This struct is 64 bytes = cache-line friendly
// GPU can read/write efficiently
```

### Chunk Management Data
```pseudocode
STRUCT ChunkCoord:
    x: int
    z: int
    
    FUNCTION GetHashCode() -> int:
        // Prime number hashing for good distribution
        RETURN (x * 73856093) XOR (z * 19349663)
    
    FUNCTION Equals(other: ChunkCoord) -> bool:
        RETURN (x == other.x) AND (z == other.z)


CLASS GrassChunk:
    // Identity
    coord: ChunkCoord
    worldOrigin: Vector3
    bounds: Bounds
    
    // CPU-side data (not sent to GPU)
    isActive: bool
    currentLOD: int
    distanceToCamera: float
    lastUpdateFrame: int
    
    // GPU-side references
    instanceDataBuffer: GraphicsBuffer
    instanceCount: int
    brgBatchID: int
    
    // Generation parameters
    seed: int
    densitySliceIndex: int
    biomeType: int
```

---

## 2. CHUNK COORDINATE SYSTEM (CPU ONLY)

### World ↔ Chunk Conversions
```pseudocode
CONSTANT chunkSize: float = 16.0  // Meters


FUNCTION WorldToChunkCoord(worldPosition: Vector3) -> ChunkCoord:
    // Floor division to get chunk grid coordinates
    chunkX = FLOOR(worldPosition.x / chunkSize)
    chunkZ = FLOOR(worldPosition.z / chunkSize)
    
    RETURN ChunkCoord(chunkX, chunkZ)


FUNCTION ChunkToWorldOrigin(coord: ChunkCoord) -> Vector3:
    // Bottom-left corner of chunk in world space
    worldX = coord.x * chunkSize
    worldZ = coord.z * chunkSize
    
    RETURN Vector3(worldX, 0, worldZ)


FUNCTION GetChunkBounds(coord: ChunkCoord, maxGrassHeight: float) -> Bounds:
    origin = ChunkToWorldOrigin(coord)
    center = origin + Vector3(chunkSize/2, maxGrassHeight/2, chunkSize/2)
    size = Vector3(chunkSize, maxGrassHeight, chunkSize)
    
    RETURN Bounds(center, size)


FUNCTION IsChunkVisible(chunk: GrassChunk, cameraPos: Vector3, viewDistance: int) -> bool:
    cameraChunk = WorldToChunkCoord(cameraPos)
    
    dx = ABS(chunk.coord.x - cameraChunk.x)
    dz = ABS(chunk.coord.z - cameraChunk.z)
    
    RETURN (dx <= viewDistance) AND (dz <= viewDistance)
```

---

## 3. BLADE PARAMETER GENERATION (CPU with Jobs)

### Random Blade Parameter Generation
```pseudocode
// This runs in JOBS (parallel on CPU)
// Generates PARAMETERS, not geometry

[BurstCompile]
STRUCT GenerateBladeParametersJob : IJobParallelFor:
    // Input (read-only)
    [ReadOnly] chunkOrigin: Vector3
    [ReadOnly] chunkSize: float
    [ReadOnly] seed: int
    [ReadOnly] bladesPerChunk: int
    [ReadOnly] terrainHeightMap: NativeArray<float>  // Optional
    [ReadOnly] densityMap: NativeArray<byte>         // Optional
    
    // Output (write-only)
    [WriteOnly] bladeParameters: NativeArray<BladeInstanceData>
    
    FUNCTION Execute(bladeIndex: int):
        // Create deterministic random generator for this blade
        random = NEW Random(seed + bladeIndex)
        
        // 1. DECIDE IF THIS BLADE EXISTS
        // Sample density at random position
        testX = random.NextFloat(0, 1)
        testZ = random.NextFloat(0, 1)
        densityValue = SampleDensityMap(densityMap, testX, testZ)
        
        IF random.NextFloat() > densityValue:
            // Mark this blade as invalid (will be skipped)
            bladeParameters[bladeIndex].height = 0
            RETURN
        
        // 2. GENERATE POSITION
        localX = random.NextFloat(0, chunkSize)
        localZ = random.NextFloat(0, chunkSize)
        
        worldX = chunkOrigin.x + localX
        worldZ = chunkOrigin.z + localZ
        
        // Sample terrain height (if available)
        terrainY = SampleTerrainHeight(terrainHeightMap, localX, localZ)
        
        // 3. GENERATE PARAMETERS (not geometry!)
        blade = NEW BladeInstanceData()
        
        blade.positionX = worldX
        blade.positionY = terrainY
        blade.positionZ = worldZ
        blade.facingAngle = random.NextFloat(0, TWO_PI)
        
        blade.height = RandomRange(random, 0.5, 1.5)      // Meters
        blade.width = RandomRange(random, 0.02, 0.05)     // Meters
        blade.curvature = RandomRange(random, -0.4, 0.4)  // Unitless
        blade.lean = RandomRange(random, -0.15, 0.15)     // Radians
        
        blade.shapeProfileID = random.NextInt(0, 8)       // 8 different shapes
        blade.colorVariationSeed = random.NextFloat()
        blade.randomSeed = random.NextFloat()
        blade.stiffness = RandomRange(random, 0.2, 0.8)
        
        blade.windPhaseOffset = random.NextFloat(0, TWO_PI)
        blade.swayFrequency = RandomRange(random, 0.8, 1.2)
        
        bladeParameters[bladeIndex] = blade


FUNCTION SampleDensityMap(densityMap: NativeArray<byte>, u: float, v: float) -> float:
    // densityMap is flattened 2D array (e.g., 64x64)
    resolution = SQRT(densityMap.Length)
    
    x = (int)(u * resolution)
    z = (int)(v * resolution)
    x = CLAMP(x, 0, resolution - 1)
    z = CLAMP(z, 0, resolution - 1)
    
    index = z * resolution + x
    byteValue = densityMap[index]
    
    RETURN byteValue / 255.0  // Convert to [0-1]


FUNCTION SampleTerrainHeight(heightMap: NativeArray<float>, localX: float, localZ: float) -> float:
    // Similar sampling logic for terrain height
    // Returns Y coordinate
    
    IF heightMap.Length == 0:
        RETURN 0  // Flat terrain
    
    // Bilinear sampling from heightmap
    // ... implementation details ...
    
    RETURN sampledHeight
```

### Scheduling the Job
```pseudocode
FUNCTION GenerateChunkBladeParameters(chunk: GrassChunk) -> JobHandle:
    // 1. Allocate output array
    bladeParams = NEW NativeArray<BladeInstanceData>(
        maxBladesPerChunk,
        Allocator.TempJob
    )
    
    // 2. Optional: Get density map for this chunk
    densityData = GetDensityMapForChunk(chunk.coord)
    
    // 3. Create job
    job = NEW GenerateBladeParametersJob {
        chunkOrigin = chunk.worldOrigin,
        chunkSize = chunkSize,
        seed = chunk.coord.GetHashCode(),
        bladesPerChunk = maxBladesPerChunk,
        terrainHeightMap = GetTerrainHeightMap(chunk),
        densityMap = densityData,
        bladeParameters = bladeParams
    }
    
    // 4. Schedule job (runs on worker threads)
    jobHandle = job.Schedule(
        arrayLength: maxBladesPerChunk,
        innerloopBatchCount: 64  // Process 64 blades per batch
    )
    
    // 5. Store references for later completion
    chunk.pendingJobHandle = jobHandle
    chunk.pendingBladeParams = bladeParams
    
    RETURN jobHandle
```

---

## 4. CHUNK MANAGER (CPU Main Thread)

### Chunk Manager Class
```pseudocode
CLASS GrassChunkManager:
    // Configuration
    chunkSize: float = 16.0
    viewDistance: int = 4              // Chunks in each direction
    maxBladesPerChunk: int = 4000
    updateFrequency: int = 10          // Update every N frames
    
    // Runtime state
    activeChunks: Dictionary<ChunkCoord, GrassChunk>
    pendingChunks: Dictionary<ChunkCoord, JobHandle>
    chunkPool: Queue<GrassChunk>
    
    // References
    cameraTransform: Transform
    batchRendererGroup: BatchRendererGroup
    
    // Frame tracking
    frameCounter: int = 0
    
    
    FUNCTION Initialize():
        // Create BRG
        batchRendererGroup = NEW BatchRendererGroup(OnPerformCulling)
        
        // Pre-allocate chunk pool
        FOR i FROM 0 TO 50:
            chunk = NEW GrassChunk()
            chunkPool.Enqueue(chunk)
    
    
    FUNCTION Update():
        frameCounter++
        
        // Don't update every frame (too expensive)
        IF frameCounter % updateFrequency != 0:
            RETURN
        
        // 1. Determine which chunks should be active
        UpdateActiveChunks()
        
        // 2. Complete pending chunk generation
        CompletePendingChunks()
        
        // 3. Update LODs based on distance
        UpdateChunkLODs()
    
    
    FUNCTION UpdateActiveChunks():
        // Get camera position
        cameraPos = cameraTransform.position
        cameraChunk = WorldToChunkCoord(cameraPos)
        
        // Determine required chunks
        requiredChunks = NEW HashSet<ChunkCoord>()
        
        FOR dz FROM -viewDistance TO viewDistance:
            FOR dx FROM -viewDistance TO viewDistance:
                coord = ChunkCoord(
                    cameraChunk.x + dx,
                    cameraChunk.z + dz
                )
                requiredChunks.Add(coord)
        
        // Activate new chunks
        FOR EACH coord IN requiredChunks:
            IF NOT activeChunks.ContainsKey(coord) AND 
               NOT pendingChunks.ContainsKey(coord):
                StartChunkGeneration(coord)
        
        // Deactivate far chunks
        chunksToRemove = NEW List<ChunkCoord>()
        
        FOR EACH (coord, chunk) IN activeChunks:
            IF NOT requiredChunks.Contains(coord):
                chunksToRemove.Add(coord)
        
        FOR EACH coord IN chunksToRemove:
            DeactivateChunk(coord)
    
    
    FUNCTION StartChunkGeneration(coord: ChunkCoord):
        // Get chunk from pool
        chunk = IF chunkPool.Count > 0 
                THEN chunkPool.Dequeue() 
                ELSE NEW GrassChunk()
        
        chunk.coord = coord
        chunk.worldOrigin = ChunkToWorldOrigin(coord)
        chunk.bounds = GetChunkBounds(coord, maxGrassHeight: 2.0)
        chunk.seed = coord.GetHashCode()
        
        // Schedule blade parameter generation (async)
        jobHandle = GenerateChunkBladeParameters(chunk)
        
        // Track pending
        pendingChunks[coord] = jobHandle
        chunk.pendingJobHandle = jobHandle
    
    
    FUNCTION CompletePendingChunks():
        chunksToActivate = NEW List<ChunkCoord>()
        
        FOR EACH (coord, jobHandle) IN pendingChunks:
            // Check if job is complete
            IF jobHandle.IsCompleted:
                chunksToActivate.Add(coord)
        
        FOR EACH coord IN chunksToActivate:
            FinishChunkGeneration(coord)
            pendingChunks.Remove(coord)
    
    
    FUNCTION FinishChunkGeneration(coord: ChunkCoord):
        chunk = GetChunkByCoord(coord)
        
        // 1. Wait for job to complete
        chunk.pendingJobHandle.Complete()
        
        // 2. Get blade parameters from job
        bladeParams = chunk.pendingBladeParams
        
        // 3. Compact array (remove invalid blades with height = 0)
        validBlades = CompactBladeArray(bladeParams)
        
        // 4. Upload to GPU
        UploadChunkToGPU(chunk, validBlades)
        
        // 5. Dispose job data
        bladeParams.Dispose()
        
        // 6. Mark active
        activeChunks[coord] = chunk
    
    
    FUNCTION DeactivateChunk(coord: ChunkCoord):
        chunk = activeChunks[coord]
        
        // Remove from BRG
        batchRendererGroup.RemoveBatch(chunk.brgBatchID)
        
        // Dispose GPU buffer
        chunk.instanceDataBuffer.Dispose()
        
        // Return to pool
        chunk.isActive = false
        chunkPool.Enqueue(chunk)
        
        activeChunks.Remove(coord)
```

---

## 5. GPU BUFFER MANAGEMENT (CPU → GPU Upload)

### Creating and Uploading Graphics Buffer
```pseudocode
FUNCTION UploadChunkToGPU(chunk: GrassChunk, bladeParams: NativeArray<BladeInstanceData>):
    // 1. Create GraphicsBuffer
    bufferSize = bladeParams.Length
    
    chunk.instanceDataBuffer = NEW GraphicsBuffer(
        target: GraphicsBuffer.Target.Structured,
        count: bufferSize,
        stride: SIZEOF(BladeInstanceData)  // 64 bytes
    )
    
    // 2. Upload data to GPU
    chunk.instanceDataBuffer.SetData(bladeParams)
    
    chunk.instanceCount = bufferSize
    
    // 3. Register with BRG
    RegisterChunkWithBRG(chunk)


FUNCTION RegisterChunkWithBRG(chunk: GrassChunk):
    // Create metadata for BRG
    // BRG needs to know about per-instance data layout
    
    metadata = NEW NativeArray<MetadataValue>(
        GetMetadataSize(),
        Allocator.Temp
    )
    
    // Set up metadata (tells BRG where to find instance data)
    SetupBRGMetadata(metadata, chunk.instanceDataBuffer)
    
    // Register batch
    batchID = batchRendererGroup.AddBatch(
        mesh: sharedGrassMesh,           // ONE mesh for all blades
        subMeshIndex: 0,
        material: grassMaterial,
        bounds: chunk.bounds,
        instanceCount: chunk.instanceCount,
        customProps: metadata,
        sceneCullingMask: 0xFFFFFFFF
    )
    
    chunk.brgBatchID = batchID
    
    metadata.Dispose()


FUNCTION CompactBladeArray(sourceArray: NativeArray<BladeInstanceData>) -> NativeArray<BladeInstanceData>:
    // Count valid blades (height > 0)
    validCount = 0
    FOR i FROM 0 TO sourceArray.Length - 1:
        IF sourceArray[i].height > 0:
            validCount++
    
    // Create compacted array
    compacted = NEW NativeArray<BladeInstanceData>(
        validCount,
        Allocator.Temp
    )
    
    writeIndex = 0
    FOR i FROM 0 TO sourceArray.Length - 1:
        IF sourceArray[i].height > 0:
            compacted[writeIndex] = sourceArray[i]
            writeIndex++
    
    RETURN compacted
```

---

## 6. LOD MANAGEMENT (CPU Decision)

### LOD System
```pseudocode
ENUM GrassLOD:
    VeryHigh = 0    // 15 segments, < 15m
    High = 1        // 10 segments, < 30m
    Medium = 2      // 6 segments,  < 60m
    Low = 3         // 3 segments,  < 100m
    VeryLow = 4     // 1 segment,   < 150m
    Culled = 5      // Don't draw,  > 150m


CLASS LODSettings:
    distances: float[] = [15, 30, 60, 100, 150]
    segmentCounts: int[] = [15, 10, 6, 3, 1]
    
    FUNCTION GetLOD(distance: float) -> GrassLOD:
        FOR i FROM 0 TO distances.Length - 1:
            IF distance < distances[i]:
                RETURN (GrassLOD)i
        
        RETURN GrassLOD.Culled
    
    FUNCTION GetSegmentCount(lod: GrassLOD) -> int:
        IF lod == GrassLOD.Culled:
            RETURN 0
        RETURN segmentCounts[(int)lod]


FUNCTION UpdateChunkLODs():
    cameraPos = cameraTransform.position
    
    FOR EACH (coord, chunk) IN activeChunks:
        // Calculate distance to chunk center
        chunkCenter = chunk.bounds.center
        distance = DISTANCE(cameraPos, chunkCenter)
        
        newLOD = lodSettings.GetLOD(distance)
        
        // Update if LOD changed
        IF newLOD != chunk.currentLOD:
            chunk.currentLOD = newLOD
            
            // Update BRG visibility
            IF newLOD == GrassLOD.Culled:
                // Hide this chunk
                batchRendererGroup.SetBatchBounds(chunk.brgBatchID, EMPTY_BOUNDS)
            ELSE:
                // Show this chunk
                batchRendererGroup.SetBatchBounds(chunk.brgBatchID, chunk.bounds)
                
                // Note: Segment count is handled in shader via distance
                // Or you can have separate material/shader variants per LOD
```

---

## 7. DENSITY MAP STREAMING (CPU Texture Management)

### Density Map Manager
```pseudocode
CLASS DensityMapManager:
    // GPU texture array (one slice per chunk)
    densityTextureArray: Texture2DArray
    densityResolution: int = 64
    maxSlices: int = 100
    
    // Tracking
    chunkToSlice: Dictionary<ChunkCoord, int>
    availableSlices: Queue<int>
    
    
    FUNCTION Initialize():
        // Create texture array on GPU
        densityTextureArray = NEW Texture2DArray(
            width: densityResolution,
            height: densityResolution,
            depth: maxSlices,
            textureFormat: TextureFormat.R8,  // Single channel
            mipChain: false
        )
        
        // Initialize available slice pool
        FOR i FROM 0 TO maxSlices - 1:
            availableSlices.Enqueue(i)
        
        // Set global shader property
        Shader.SetGlobalTexture("_DensityMapArray", densityTextureArray)
    
    
    FUNCTION AllocateDensitySliceForChunk(coord: ChunkCoord):
        IF chunkToSlice.ContainsKey(coord):
            RETURN  // Already allocated
        
        IF availableSlices.Count == 0:
            // Need to evict old chunk
            EvictOldestDensitySlice()
        
        sliceIndex = availableSlices.Dequeue()
        
        // Generate density data
        densityData = GenerateDensityData(coord)
        
        // Upload to GPU texture array
        UploadDensitySlice(densityData, sliceIndex)
        
        // Track mapping
        chunkToSlice[coord] = sliceIndex
        
        densityData.Dispose()
    
    
    FUNCTION GenerateDensityData(coord: ChunkCoord) -> NativeArray<byte>:
        pixelCount = densityResolution * densityResolution
        densityData = NEW NativeArray<byte>(pixelCount, Allocator.Temp)
        
        worldOrigin = ChunkToWorldOrigin(coord)
        
        FOR y FROM 0 TO densityResolution - 1:
            FOR x FROM 0 TO densityResolution - 1:
                // World position of this density pixel
                worldX = worldOrigin.x + (x / densityResolution) * chunkSize
                worldZ = worldOrigin.z + (y / densityResolution) * chunkSize
                
                // Generate density using noise
                density = GenerateDensityValue(worldX, worldZ)
                
                // Convert to byte [0-255]
                index = y * densityResolution + x
                densityData[index] = (byte)(density * 255)
        
        RETURN densityData
    
    
    FUNCTION GenerateDensityValue(worldX: float, worldZ: float) -> float:
        // Use layered Perlin noise
        scale1 = 0.05
        scale2 = 0.1
        
        noise1 = PerlinNoise(worldX * scale1, worldZ * scale1)
        noise2 = PerlinNoise(worldX * scale2, worldZ * scale2) * 0.5
        
        density = noise1 + noise2
        density = CLAMP(density, 0, 1)
        
        // Optional: Apply biome mask, height mask, etc.
        
        RETURN density
    
    
    FUNCTION UploadDensitySlice(densityData: NativeArray<byte>, sliceIndex: int):
        // Upload to specific slice in texture array
        densityTextureArray.SetPixelData(
            data: densityData,
            mipLevel: 0,
            sourceDataStartIndex: 0,
            slice: sliceIndex
        )
        
        // Apply changes to GPU
        densityTextureArray.Apply(updateMipmaps: false, makeNoLongerReadable: false)
    
    
    FUNCTION GetDensitySliceForChunk(coord: ChunkCoord) -> int:
        IF chunkToSlice.ContainsKey(coord):
            RETURN chunkToSlice[coord]
        
        RETURN -1  // Not allocated
    
    
    FUNCTION FreeDensitySlice(coord: ChunkCoord):
        IF NOT chunkToSlice.ContainsKey(coord):
            RETURN
        
        sliceIndex = chunkToSlice[coord]
        chunkToSlice.Remove(coord)
        
        availableSlices.Enqueue(sliceIndex)
```

---

## 8. WIND TEXTURE GENERATION (CPU Preparation, GPU Sampling)

### Wind Texture Manager
```pseudocode
CLASS WindTextureManager:
    windTexture: RenderTexture
    windResolution: int = 512
    windComputeShader: ComputeShader
    
    // Wind parameters
    windScale: float = 0.05
    windSpeed: float = 1.0
    windDirection: Vector2 = Vector2(1, 0)
    
    
    FUNCTION Initialize():
        // Create wind texture (updated via compute shader)
        windTexture = NEW RenderTexture(
            width: windResolution,
            height: windResolution,
            depth: 0,
            format: RenderTextureFormat.ARGBFloat
        )
        windTexture.enableRandomWrite = true
        windTexture.wrapMode = TextureWrapMode.Repeat
        windTexture.Create()
        
        // Set global shader property
        Shader.SetGlobalTexture("_WindTexture", windTexture)
    
    
    FUNCTION UpdateWindTexture(currentTime: float):
        // Dispatch compute shader to update wind
        kernelIndex = windComputeShader.FindKernel("UpdateWind")
        
        // Set parameters
        windComputeShader.SetFloat("_Time", currentTime)
        windComputeShader.SetFloat("_WindScale", windScale)
        windComputeShader.SetFloat("_WindSpeed", windSpeed)
        windComputeShader.SetVector("_WindDirection", windDirection)
        windComputeShader.SetTexture(kernelIndex, "_WindTexture", windTexture)
        
        // Dispatch (8x8 thread groups)
        threadGroups = windResolution / 8
        windComputeShader.Dispatch(kernelIndex, threadGroups, threadGroups, 1)
        
        // Wind texture is now updated on GPU
        // Grass shaders will sample it
```

---

## 9. MAIN EXECUTION FLOW (CPU Thread)

### Initialization
```pseudocode
FUNCTION Awake():
    // 1. Initialize subsystems
    chunkManager = NEW GrassChunkManager()
    chunkManager.Initialize()
    
    densityManager = NEW DensityMapManager()
    densityManager.Initialize()
    
    windManager = NEW WindTextureManager()
    windManager.Initialize()
    
    lodSettings = NEW LODSettings()


FUNCTION Start():
    // Initial chunk generation around camera
    chunkManager.UpdateActiveChunks()
```

### Update Loop
```pseudocode
FUNCTION Update():
    // 1. Update wind texture (compute shader on GPU)
    windManager.UpdateWindTexture(Time.time)
    
    // 2. Update chunk streaming (every N frames)
    chunkManager.Update()
    
    // 3. Handle chunk generation completion
    chunkManager.CompletePendingChunks()
```

### Fixed Update (Optional)
```pseudocode
FUNCTION FixedUpdate():
    // Physics-rate updates if needed
    // Generally not needed for grass
```

---

## 10. BRG CULLING CALLBACK (CPU, Called by Unity)

### Culling Callback
```pseudocode
FUNCTION OnPerformCulling(context: BatchCullingContext) -> JobHandle:
    // This is called by Unity before rendering
    // You can control which batches are visible
    
    // For now: simple implementation (cull by distance)
    cameraPos = GetCameraPosition(context)
    
    FOR EACH (coord, chunk) IN activeChunks:
        distance = DISTANCE(cameraPos, chunk.bounds.center)
        
        IF distance > maxRenderDistance:
            // Mark batch as invisible
            // (Implementation depends on BRG API)
            MarkBatchInvisible(chunk.brgBatchID)
        ELSE:
            MarkBatchVisible(chunk.brgBatchID)
    
    // Return default job handle (no jobs scheduled)
    RETURN default(JobHandle)


// Advanced: Frustum culling
FUNCTION OnPerformCulling_Advanced(context: BatchCullingContext) -> JobHandle:
    // Get culling planes from camera
    cullingPlanes = context.cullingPlanes
    
    // Schedule job to test each chunk's bounds
    cullingJob = NEW FrustumCullingJob {
        planes = cullingPlanes,
        chunkBounds = GetAllChunkBounds(),
        visibilityResults = NEW NativeArray<int>(chunkCount, Allocator.TempJob)
    }
    
    jobHandle = cullingJob.Schedule(chunkCount, 32)
    
    RETURN jobHandle
```

---

## 11. MEMORY MANAGEMENT (CPU Responsibilities)

### Resource Pooling
```pseudocode
CLASS ResourcePool:
    chunkPool: Queue<GrassChunk>
    nativeArrayPool: Dictionary<int, Queue<NativeArray>>
    
    
    FUNCTION GetChunk() -> GrassChunk:
        IF chunkPool.Count > 0:
            RETURN chunkPool.Dequeue()
        ELSE:
            RETURN NEW GrassChunk()
    
    
    FUNCTION ReturnChunk(chunk: GrassChunk):
        // Clear chunk data
        chunk.isActive = false
        chunk.instanceCount = 0
        
        // Return to pool
        chunkPool.Enqueue(chunk)
    
    
    FUNCTION GetNativeArray<T>(size: int) -> NativeArray<T>:
        IF nativeArrayPool.ContainsKey(size) AND nativeArrayPool[size].Count > 0:
            RETURN nativeArrayPool[size].Dequeue()
        ELSE:
            RETURN NEW NativeArray<T>(size, Allocator.Persistent)
    
    
    FUNCTION ReturnNativeArray<T>(array: NativeArray<T>):
        size = array.Length
        
        IF NOT nativeArrayPool.ContainsKey(size):
            nativeArrayPool[size] = NEW Queue<NativeArray>()
        
        nativeArrayPool[size].Enqueue(array)
```

### Cleanup
```pseudocode
FUNCTION OnDestroy():
    // 1. Dispose all chunks
    FOR EACH chunk IN activeChunks.Values:
        chunk.instanceDataBuffer?.Dispose()
    
    // 2. Dispose BRG
    batchRendererGroup?.Dispose()
    
    // 3. Dispose pooled native arrays
    FOR EACH queue IN nativeArrayPool.Values:
        FOR EACH array IN queue:
            array.Dispose()
    
    // 4. Dispose textures
    windManager.windTexture?.Release()
    densityManager.densityTextureArray?.Dispose()
```

---

## 12. PROFILING AND DEBUG (CPU Monitoring)

### Performance Monitoring
```pseudocode
CLASS GrassProfiler:
    FUNCTION LogStatistics():
        activeChunkCount = chunkManager.activeChunks.Count
        totalBlades = 0
        totalVertices = 0
        
        FOR EACH chunk IN chunkManager.activeChunks.Values:
            totalBlades += chunk.instanceCount
            totalVertices += chunk.instanceCount * 30  // 15 segments × 2 verts
        
        gpuMemory = CalculateGPUMemoryUsage()
        
        LOG("=== Grass Statistics ===")
        LOG("Active Chunks: " + activeChunkCount)
        LOG("Total Blades: " + totalBlades)
        LOG("Total Vertices: " + totalVertices)
        LOG("GPU Memory: " + (gpuMemory / 1024 / 1024) + " MB")
        LOG("FPS: " + (1.0 / Time.deltaTime))
    
    
    FUNCTION CalculateGPUMemoryUsage() -> int:
        bytesPerBlade = SIZEOF(BladeInstanceData)  // 64 bytes
        totalBlades = 0
        
        FOR EACH chunk IN chunkManager.activeChunks.Values:
            totalBlades += chunk.instanceCount
        
        RETURN totalBlades * bytesPerBlade
```

---

## CRITICAL CPU RULES

### ✅ DO on CPU:
1. Generate blade PARAMETERS (position, height, width, etc.)
2. Manage chunk streaming and visibility
3. Upload data to GPU once
4. Make LOD decisions based on distance
5. Schedule Jobs for parallel work
6. Handle memory allocation/deallocation
7. Manage texture arrays and buffers

### ❌ DON'T on CPU:
1. Build final vertex positions
2. Compute curve mathematics every frame
3. Animate grass
4. Update individual blade transforms
5. Touch geometry after upload
6. Use GetComponent in Update
7. Allocate memory in Update/LateUpdate
8. Use LINQ or foreach on large collections

---

## OPTIMIZATION CHECKLIST

### Memory Optimization
```pseudocode
RULES:
- Reuse NativeArrays (don't allocate every frame)
- Pool GrassChunk objects
- Use Allocator.TempJob for job outputs
- Dispose NativeArrays immediately after use
- Keep BladeInstanceData struct cache-aligned (64 bytes)
- Limit active chunks to reasonable number (< 100)
```

### CPU Performance
```pseudocode
RULES:
- Use Jobs + Burst for blade parameter generation
- Update chunks every N frames (not every frame)
- Use Dictionary with good hash function
- Cache Transform references
- Avoid string operations in Update
- Profile with Unity Profiler regularly
```

### Job System Best Practices
```pseudocode
RULES:
- Use IJobParallelFor for blade generation
- Schedule jobs early, complete late
- Use [BurstCompile] attribute
- Avoid managed objects in jobs
- Use NativeArray, not List/Array
- Check JobHandle.IsCompleted before waiting
```

---

## IMPLEMENTATION PRIORITY (CPU SIDE)

### Week 1-2: Foundation
1. ChunkCoord struct and conversion functions
2. BladeInstanceData struct (finalize layout)
3. Chunk manager skeleton
4. Coordinate system tests

### Week 3-4: Job System
1. GenerateBladeParametersJob implementation
2. Job scheduling and completion
3. Buffer allocation and disposal
4. Testing with small chunk counts

### Week 5-6: BRG Integration
1. GraphicsBuffer creation
2. BRG batch registration
3. Upload pipeline
4. Basic culling callback

### Week 7-8: Streaming
1. Density map generation
2. Texture array upload
3. Chunk activation/deactivation
4. Memory pooling

### Week 9-10: Polish
1. LOD system
2. Performance profiling
3. Memory optimization
4. Debugging tools
