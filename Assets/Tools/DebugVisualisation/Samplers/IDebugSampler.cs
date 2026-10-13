using GhostOfTsushima.Runtime;
using System.Collections.Generic;
using UnityEngine;
namespace DebugVisualisation.Samplers
{
    public interface IWindSampler
    {
        Vector3 SampleDirection(Vector3 worldPos);

        float SampleStrength(Vector3 worldPos);

        float GustIntensity { get; }
        IReadOnlyList<VorticleInfo> ActiveVorticles { get; }
    }

    public interface IVoronoiSampler
    {
        float SampleDensity(Vector3 worldPos);

        float SampleHumidity(Vector3 worldPos);

        float SampleFertility(Vector3 worldPos);

        int SampleCellID(Vector3 worldPos);

        IReadOnlyList<Vector3> CellCenters { get; }
    }

    public interface IChunkSampler
    {
        IReadOnlyList<ChunkInfo> ActiveChunks { get; }
    }

 
    public struct VorticleInfo
    {
        public Vector3 position;
        public Vector3 direction;     
        public float radius;
        public float strength;
        public float decayT;        
        public bool IsOmni => direction.sqrMagnitude < 0.001f;
    }

    public struct ChunkInfo
    {
        public Bounds bounds;
        public int lodLevel;
        public int instanceCount;
        public int coordinate_x;
        public int coordinate_z;
    }
}