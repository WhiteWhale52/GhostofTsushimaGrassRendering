using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostOfTsushima.Runtime
{

    struct ChunkCoordinate {
        public uint x, z;



		public override int GetHashCode(){
			uint hash = x;
			hash ^= z + 0x9e3779b9 + (hash << 6) + (hash >> 2); 
			return (int)hash;
		}

        public ChunkCoordinate(int x, int z) : this() { }

        public bool equals(ChunkCoordinate chunkCoordinate){
            return x == chunkCoordinate.x && z == chunkCoordinate.z;
        }
    }

    struct GrassChunk {
		public ChunkCoordinate coordinate;
		public float3 worldOrigin;
		public Bounds bounds;

        // CPU-side data
        bool isActive;
        float distanceToCamera;
        int lastUpdateFrame;

        NativeArray<GrassBladeInstanceData> grassBladeInstances;

		Mesh m_GrassMesh;
		Material m_GrassMaterial;
		BatchMeshID m_MeshID;
		BatchMaterialID m_MaterialID;
		
        int numOfGrassBlades;

        int biomeType;

		GraphicsBuffer m_InstanceData;
		BatchID m_BatchID;

        int currentLODLevel;


	}
    public class GrassChunkSystemManager : MonoBehaviour
    {
        // Configurations 
         const float chunkSize = 16.0f;
            // Number of Chunks visible in each direction
            int viewDistance = 3;
            int maxBladesPerChunk = 5000;
        //

        // Runtime Data
            Dictionary<ChunkCoordinate, GrassChunk> activeChunks;
            Queue<GrassChunk> chunkQueue;
        //

        // References
        Transform cameraTransform = Camera.main.transform;
        Mesh TemplateGrassMesh;
        Material TemplateGrassMaterial;
        //

        ChunkCoordinate WorldToChunkCoordinate(Vector3 t_WorldPosition){
            int chunkX = (int)math.floor(t_WorldPosition.x / chunkSize);

            int chunkZ = (int)math.floor(t_WorldPosition.z / chunkSize);


            return new ChunkCoordinate(chunkX, chunkZ);

		}


        Vector3 ChunkCoordinateToWorld(ChunkCoordinate chunkCoordinate) {
            float worldX = chunkCoordinate.x * chunkSize;
            float worldZ = chunkCoordinate.x * chunkSize;

            return new Vector3(worldX,0, worldZ);
        }

        Bounds GetChunkBounds(ChunkCoordinate chunkCoordinate, float maxGrassHeight){
            Vector3 origin = ChunkCoordinateToWorld(chunkCoordinate);
            Vector3 centre = origin + new Vector3(chunkSize / 2, maxGrassHeight / 2, chunkSize / 2);

            Vector3 size = new Vector3(chunkSize, maxGrassHeight, chunkSize);

            return new Bounds(centre,size);

        }

        bool IsChunkVisible(GrassChunk chunk, Vector3 cameraPosition, int viewDistance) {
            ChunkCoordinate cameraChunk = WorldToChunkCoordinate(cameraPosition);

            float deltaX = math.abs(chunk.coordinate.x - cameraChunk.x);
            float deltaZ = math.abs(chunk.coordinate.z - cameraChunk.z);

            return (deltaX <= viewDistance) && (deltaZ <= viewDistance);
        }

    }
}
