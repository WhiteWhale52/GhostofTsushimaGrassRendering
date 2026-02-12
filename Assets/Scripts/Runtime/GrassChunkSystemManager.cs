using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostOfTsushima.Runtime
{
    public class GrassChunkSystemManager : MonoBehaviour
    {
        // Configurations 
            float chunkSize = 16.0f;
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

    }

    struct ChunkCoordinate {
        uint x;
        uint z;

		public uint GetHashCode(){
			uint hash = x;
			hash ^= z + 0x9e3779b9 + (hash << 6) + (hash >> 2); 
			return hash;
		}

        public bool equals(ChunkCoordinate chunkCoordinate){
            return x == chunkCoordinate.x && z == chunkCoordinate.z;
        }
    }

    struct GrassChunk {
		public ChunkCoordinate coordinate;
		public float3 worldOrigin;
		public Bounds bounds;

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
}
