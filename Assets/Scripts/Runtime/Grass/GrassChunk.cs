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


        public override int GetHashCode()
        {
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

        public static ChunkCoordinate operator +(ChunkCoordinate a, ChunkCoordinate b)
        {
            return new ChunkCoordinate(a.x + b.x, a.z + b.z);
        }
    }

    public class GrassChunk
    {
        public ChunkCoordinate coordinate;
        public float3 worldOrigin;
        public Bounds bounds;



        public GraphicsBuffer graphicsBufferInstanceData;
        public int instanceCount;
        public BatchID m_BatchID;

        public int seed;
        public int currentLODLevel;

        public bool isGenerated;

        public VoronoiCell[] voronoiCells;
        public SoilData soilData;

        public void Dispose()
        {
            graphicsBufferInstanceData?.Release();
            graphicsBufferInstanceData = null;
        }

    }
}

