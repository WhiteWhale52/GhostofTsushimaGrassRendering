using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostOfTsushima.Runtime
{
    public class ChunkSystemManager : MonoBehaviour
    {
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
        
        }

        // Update is called once per frame
        void Update()
        {
        
        }
    }

    struct ChunkCoordinate {
        uint x;
        uint z;

        uint GetHashCode(){
			uint hash = x;
			hash ^= z + 0x9e3779b9 + (hash << 6) + (hash >> 2); 
			return hash;
		}

        public bool equals(ChunkCoordinate chunkCoordinate){
            return x == chunkCoordinate.x && z == chunkCoordinate.z;
        }
    }

    struct GrassChunk {
        ChunkCoordinate coordinate;
        float3 worldOrigin;
        Bounds bounds;

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
