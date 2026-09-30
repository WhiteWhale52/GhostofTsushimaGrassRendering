using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace GhostOfTsushima.Runtime
{

    public class GrassChunkSystemManager
    {
        // Runtime Data
        private readonly Dictionary<ChunkCoordinate, GrassChunk> activeChunks = new();
        private readonly Queue<GrassChunk> chunkQueue;

        // Configurations 
        private const float CHUNK_SIZE = 16.0f;
        private const int VIEW_DISTANCE = 3;
        private const int MAX_BLADES_PER_CHUNK = 500;
        //


        // References
        private readonly Transform cameraTransform;
            
        private readonly BatchRendererGroup _brg;


		public BatchMeshID batchMeshID;
		public BatchMaterialID batchMaterialID;


        public GrassChunkSystemManager(BatchRendererGroup brg, Transform camera)
		{
			_brg = brg;
			cameraTransform = camera;
		}
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


		ChunkCoordinate WorldToChunkCoordinate(Vector3 t_WorldPosition){
			
            int chunkX = Mathf.FloorToInt(t_WorldPosition.x / CHUNK_SIZE);

            int chunkZ = Mathf.FloorToInt(t_WorldPosition.z / CHUNK_SIZE);


            return new ChunkCoordinate(chunkX, chunkZ);

		}

		private Bounds ChunkBounds(ChunkCoordinate chunkCoordinate)
		{
			Vector3 centre = new Vector3((chunkCoordinate.x + 0.5f) * CHUNK_SIZE, 0, (chunkCoordinate.z + 0.5f) * CHUNK_SIZE);
			Vector3 size = new Vector3(CHUNK_SIZE, 100f, CHUNK_SIZE);
			return new Bounds(centre, size);
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
			var cameraPosition = cameraTransform.position;
			var cameraChunkCoordinate = WorldToChunkCoordinate(cameraPosition);

			var desiredCoords = new HashSet<ChunkCoordinate>();


            for (int dz = -VIEW_DISTANCE; dz <= VIEW_DISTANCE; dz++)
				for (int dx = -VIEW_DISTANCE; dx <= VIEW_DISTANCE; dx++)
					desiredCoords.Add(cameraChunkCoordinate + new ChunkCoordinate(dx, dz));


            // Activate new chunks
            foreach (var coord in desiredCoords)
			{
				if (!activeChunks.ContainsKey(coord))
				{
					LoadChunk(coord);
				}
			}

			// Deactivate far chunks
			List<ChunkCoordinate> toRemove = new List<ChunkCoordinate>();
			foreach (var key in activeChunks.Keys)
			{
				if (!desiredCoords.Contains(key))
				{
					toRemove.Add(key);
				}
			}

			foreach (var coord in toRemove)
			{
				UnloadChunk(coord);
			}
		}

		public Dictionary<ChunkCoordinate, GrassChunk> GetActiveChunks() =>  activeChunks;

        private void LoadChunk(ChunkCoordinate coord)
		{
            var chunk = new GrassChunk
            {
                coordinate = coord,
                bounds = ChunkBounds(coord),
                isGenerated = false,
                currentLODLevel = 0
            };

            // Populate instance data — this is where terrain sampling happens
            GrassInstancePopulator.Populate(chunk, batchMeshID, batchMaterialID, _brg);

            activeChunks[coord] = chunk;
        }

		private void UnloadChunk(ChunkCoordinate coord)
		{
			if (!activeChunks.TryGetValue(coord, out GrassChunk chunk))
				return;

			// Remove from BRG
			_brg.RemoveBatch(chunk.m_BatchID);
			Debug.Log($"Chunk deactivated with {chunk.instanceCount} blades, batchID: {chunk.m_BatchID.value} at  x:{chunk.coordinate.x}, z: {chunk.coordinate.z}");

			chunk.Dispose();

			chunk.isGenerated = false;

			chunkQueue.Enqueue(chunk);
			activeChunks.Remove(coord);
		}


		public void Dispose()
		{
			foreach (var chunk in activeChunks.Values)
			{
				_brg.RemoveBatch(chunk.m_BatchID);
                chunk.Dispose();
			}
			activeChunks.Clear();
		}
		
	}
}
