using System.Collections.Generic;
using System.Linq;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostOfTsushima.Runtime
{

	public struct ChunkCoordinate : System.IEquatable<ChunkCoordinate>
	{
		public int x, z;

		public ChunkCoordinate(int x, int z)
		{
			this.x = x;
			this.z = z;
		}


		public override int GetHashCode(){
			uint hash = (uint)x;
			hash ^= (uint)z + 0x9e3779b9 + (hash << 6) + (hash >> 2); 
			return (int)hash;
		}


		public bool Equals(ChunkCoordinate other)  
		{
			return x == other.x && z == other.z;
		}

		public override bool Equals(object obj)  
		{
			return obj is ChunkCoordinate other && Equals(other);
		}

		public static bool operator ==(ChunkCoordinate a, ChunkCoordinate b)
		{
			return a.Equals(b);
		}

		public static bool operator !=(ChunkCoordinate a, ChunkCoordinate b)
		{
			return !a.Equals(b);
		}
	}

    public class GrassChunk {
		public ChunkCoordinate coordinate;
		public float3 worldOrigin;
		public Bounds bounds;

		// CPU-side data
		public bool isActive;
		public float distanceToCamera;
		public int lastUpdateFrame;

        //NativeArray<GrassBladeInstanceData> grassBladeInstances;

		public GraphicsBuffer graphicsBufferInstanceData;
		public int instanceCount;
		public BatchID m_BatchID;

		public int seed;
		public int currentLODLevel;
		public void Dispose()
		{
			graphicsBufferInstanceData?.Dispose();
			//graphicsBufferInstanceData = null;
		}

	}
    public class GrassChunkSystemManager
    {
        // Configurations 
        private const float CHUNK_SIZE = 16.0f;
        private const int VIEW_DISTANCE = 3;
        private const int MAX_BLADES_PER_CHUNK = 500;
        //

        // Runtime Data
            Dictionary<ChunkCoordinate, GrassChunk> activeChunks;
            Queue<GrassChunk> chunkQueue;
        //

        // References
            public Transform cameraTransform = Camera.main.transform;
            
            BatchRendererGroup brg;

		//

		public int ActiveChunkCount => activeChunks.Count;
		public int TotalBladeCount
		{
			get
			{
				int total = 0;
				foreach (var chunk in activeChunks.Values)
					total += chunk.instanceCount;
				return total;
			}
		}

		public GrassChunkSystemManager(BatchRendererGroup brg, Transform camera)
		{
			this.brg = brg;
			this.cameraTransform = camera;

			activeChunks = new Dictionary<ChunkCoordinate, GrassChunk>();
			chunkQueue = new Queue<GrassChunk>();
		}

		ChunkCoordinate WorldToChunkCoordinate(Vector3 t_WorldPosition){
			
            int chunkX = (int)math.floor(t_WorldPosition.x / CHUNK_SIZE);

            int chunkZ = (int)math.floor(t_WorldPosition.z / CHUNK_SIZE);


            return new ChunkCoordinate(chunkX, chunkZ);

		}


        Vector3 ChunkCoordinateToWorld(ChunkCoordinate chunkCoordinate) {
            float worldX = chunkCoordinate.x * CHUNK_SIZE;
            float worldZ = chunkCoordinate.z * CHUNK_SIZE;

            return new Vector3(worldX,0, worldZ);
        }

        Bounds GetChunkBounds(ChunkCoordinate chunkCoordinate, float maxGrassHeight){
            Vector3 origin = ChunkCoordinateToWorld(chunkCoordinate);
            Vector3 centre = origin + new Vector3(CHUNK_SIZE / 2, maxGrassHeight / 2, CHUNK_SIZE / 2);

            Vector3 size = new Vector3(CHUNK_SIZE, maxGrassHeight, CHUNK_SIZE);

            return new Bounds(centre,size);

        }


		public void UpdateVisibleChunks()
		{
			ChunkCoordinate cameraChunkCoordinate = WorldToChunkCoordinate(cameraTransform.position);
			Debug.Log($"Camera chunk: x: {cameraChunkCoordinate.x}  z: {cameraChunkCoordinate.z}");
			HashSet<ChunkCoordinate> requiredChunks = new HashSet<ChunkCoordinate>();

			for (int dz = -VIEW_DISTANCE; dz <= VIEW_DISTANCE; dz++)
			{
				for (int dx = -VIEW_DISTANCE; dx <= VIEW_DISTANCE; dx++)
				{
					ChunkCoordinate coord = new ChunkCoordinate(
						(int)cameraChunkCoordinate.x + dx,
						(int)cameraChunkCoordinate.z + dz
					);
					requiredChunks.Add(coord);
				}
			}

			// Activate new chunks
			foreach (var coord in requiredChunks)
			{
				if (!activeChunks.ContainsKey(coord))
				{
					CreateChunk(coord);
				}
			}

			// Deactivate far chunks
			List<ChunkCoordinate> toRemove = new List<ChunkCoordinate>();
			foreach (var kvp in activeChunks)
			{
				if (!requiredChunks.Contains(kvp.Key))
				{
					toRemove.Add(kvp.Key);
				}
			}

			foreach (var coord in toRemove)
			{
				DeactivateChunk(coord);
			}
		}

		public ref Dictionary<ChunkCoordinate, GrassChunk> GetActiveChunks(){
			return ref activeChunks;
		}

		private void CreateChunk(ChunkCoordinate coord)
		{
			// Get from pool or create new
			GrassChunk chunk = chunkQueue.Count > 0 ? chunkQueue.Dequeue() : new GrassChunk();
			Debug.Log($"Creating chunk at ({coord.x}, {coord.z})");
			chunk.coordinate = coord;
			chunk.worldOrigin = ChunkCoordinateToWorld(coord);
			chunk.bounds = GetChunkBounds(coord, 2.0f);
			chunk.seed = coord.GetHashCode();


			PopulateChunkBladesBuffer(chunk);
			Debug.Log($"Chunk created with {chunk.instanceCount} blades, batchID: {chunk.m_BatchID.value}");
			

			chunk.isActive = true;
			activeChunks[coord] = chunk;
		}

		private void DeactivateChunk(ChunkCoordinate coord)
		{
			if (!activeChunks.TryGetValue(coord, out GrassChunk chunk))
				return;

			// Remove from BRG
			brg.RemoveBatch(chunk.m_BatchID);
			Debug.Log($"Chunk deactivated with {chunk.instanceCount} blades, batchID: {chunk.m_BatchID.value} at  x:{chunk.coordinate.x}, z: {chunk.coordinate.z}");

			chunk.Dispose();

			chunk.isActive = false;

			chunkQueue.Enqueue(chunk);
			activeChunks.Remove(coord);
		}


		[BurstCompile]
		private void PopulateChunkBladesBuffer(GrassChunk chunk)
		{

			var bladeParameters = new NativeArray<GrassBladeInstanceData>(MAX_BLADES_PER_CHUNK, Allocator.TempJob);

			PopulateChunkBladesJob job = new PopulateChunkBladesJob
			{
				bladeInstances = bladeParameters,
				chunkOrigin = chunk.worldOrigin,
				chunkSize = CHUNK_SIZE,
				seed = chunk.seed
			};
			JobHandle handle = job.Schedule(MAX_BLADES_PER_CHUNK, 64);
			handle.Complete();
			
			//bladeParameters = null;

			// In this simple example, the instance data is placed into the buffer like this:
			// Offset | Description
			//      0 | 64 bytes of zeroes, so loads from address 0 return zeroes
			//     64 | 32 uninitialized bytes to make working with SetData easier, otherwise unnecessary
			//     96 | unity_ObjectToWorld, three packed float3x4 matrices
			//    240 | unity_WorldToObject, three packed float3x4 matrices

			// Calculates start addresses for the different instanced properties. unity_ObjectToWorld starts
			// at address 96 instead of 64, because the computeBufferStartIndex parameter of SetData
			// is expressed as source array elements, so it is easier to work in multiples of sizeof(PackedMatrix).
			int sizeOfBladeData = UnsafeUtility.SizeOf<GrassBladeInstanceData>();
			int intBufferSize = BufferCountForInstances(sizeOfBladeData, MAX_BLADES_PER_CHUNK, 64);
			//    uint byteAddressColor = byteAddressWorldToObject + kSizeOfPackedMatrix * (uint) smallNumberOfGrassBlades;

			chunk.graphicsBufferInstanceData = new GraphicsBuffer(
				GraphicsBuffer.Target.Raw,
				intBufferSize,
				sizeof(int)
			);

			// Upload the instance data to the GraphicsBuffer so the shader can load them.
			var zero = new float4x4[1] { float4x4.zero };
			chunk.graphicsBufferInstanceData.SetData(zero, 0, 0, 1);
			chunk.graphicsBufferInstanceData.SetData(bladeParameters, 0, 1 , bladeParameters.Length);

			chunk.instanceCount = MAX_BLADES_PER_CHUNK;
			// Set up metadata values to point to the instance data. Set the most significant bit 0x80000000 in each
			// which instructs the shader that the data is an array with one value per instance, indexed by the instance index.
			// Any metadata values that the shader uses that are not set here will be 0. When a value of 0 is used with
			// UNITY_ACCESS_DOTS_INSTANCED_PROP (i.e. without a default), the shader interprets the
			// 0x00000000 metadata value and loads from the start of the buffer. The start of the buffer is
			// a zero matrix so this sort of load is guaranteed to return zero, which is a reasonable default value.
			var metadata = new NativeArray<MetadataValue>(10, Allocator.Temp);
			int offset = 64; // Start after zero block

			metadata[0] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Position"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 16; // float3 = 12 bytes (but aligned to 16)

			metadata[1] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_FacingAngle"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[2] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Height"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[3] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Width"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[4] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Curvature"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[5] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Lean"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[6] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_ColorSeed"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[7] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_BladeHash"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[8] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_Stiffness"),
				Value = 0x80000000 | (uint)offset
			};
			offset += 4;

			metadata[9] = new MetadataValue
			{
				NameID = Shader.PropertyToID("_WindPhaseOffset"),
				Value = 0x80000000 | (uint)offset
			};

			// Finally, create a batch for the instances and make the batch use the GraphicsBuffer with the
			// instance data as well as the metadata values that specify where the properties are.
			Debug.Log("BatchRendererGroup in the grass chunk manager system class " + brg);
			chunk.m_BatchID = brg.AddBatch(metadata, chunk.graphicsBufferInstanceData.bufferHandle);
			bladeParameters.Dispose();
			metadata.Dispose();
		}

		// Raw buffers are allocated in ints. This is a utility method that calculates
		// the required number of ints for the data.
		private int BufferCountForInstances(int bytesPerInstance, int numInstances, int extraBytes = 0)
		{
			bytesPerInstance = (bytesPerInstance + sizeof(int) - 1) / sizeof(int) * sizeof(int);
			extraBytes = (extraBytes + sizeof(int) - 1) / sizeof(int) * sizeof(int);
			int totalBytes = bytesPerInstance * numInstances + extraBytes;
			return totalBytes / sizeof(int);
		}

		[BurstCompile]
		private struct PopulateChunkBladesJob : IJobParallelFor
		{
			[WriteOnly] public NativeArray<GrassBladeInstanceData> bladeInstances;

			public float3 chunkOrigin;
			public float chunkSize;
			public int seed;

			public void Execute(int index)
			{
				var random = new Unity.Mathematics.Random((uint)(seed + index + 1));

				// Random position within chunk
				float3 randomPos = chunkOrigin + new float3(
					random.NextFloat() * chunkSize,
					0.0f,
					random.NextFloat() * chunkSize
				);

				bladeInstances[index] = new GrassBladeInstanceData
				{
					position = randomPos,
					facingAngle = random.NextFloat() * 2.0f * math.PI,

					height = random.NextFloat(0.05f, 0.15f),
					width = random.NextFloat(0.005f, 0.01f),
					curvatureStrength = random.NextFloat(-0.1f, 0.1f),
					lean = random.NextFloat(-0.08f, 0.08f),

					shapeProfileID = random.NextInt(0, 8),
					colorVariationSeed = random.NextFloat(),
					bladeHash = random.NextFloat(),
					stiffness = random.NextFloat(0.3f, 0.8f),

					windPhaseOffset = random.NextFloat(0, 2f * math.PI),
					windStrength = random.NextFloat(0.8f, 1.2f),
					padding01 = 0,
					padding02 = 0
				};
				Debug.Log($"Blade {index} has position {randomPos}");
			}
		}
		public void Dispose()
		{
			foreach (var chunk in activeChunks.Values)
			{
				chunk.Dispose();
			}
			activeChunks.Clear();
		}
		
	}
}
