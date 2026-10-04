using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace GhostOfTsushima.Runtime
{
    public static class VoronoiClusteringSystem
    {
        public const int DefaultCellCount = 12;

        public const float EdgeFalloffFraction = 0.05f;
        public static void ApplyClustering(NativeArray<GrassBladeInstanceData> bladeInstanceDatas, int2 chunkCoord, float chunkSize, float3 chunkWorldOrigin, uint globalSeed, int cellCount, out NativeArray<VoronoiCell> cells)
        {
            cells = new NativeArray<VoronoiCell>(cellCount, Allocator.Persistent);
            VoronoiGenerator.GenerateCells(chunkCoord, chunkSize, cellCount, globalSeed, cells);

            var job = new AssignVoronoiCellsJob
            {
                bladeData = bladeInstanceDatas,
                cells = cells,
                chunkWorldOrigin = chunkWorldOrigin,
                edgeFalloffWidth = chunkSize * EdgeFalloffFraction
            };

            // Batch size 64: each blade does O(cellCount) work — small enough
            // that 64 per batch is a reasonable scheduling unit
            job.Schedule(bladeInstanceDatas.Length, 64).Complete();
        }

        [BurstCompile]
        public struct AssignVoronoiCellsJob : IJobParallelFor
        {
             public NativeArray<GrassBladeInstanceData> bladeData;
            [ReadOnly] public NativeArray<VoronoiCell> cells;
            [ReadOnly] public float3 chunkWorldOrigin;
            [ReadOnly] public float edgeFalloffWidth;
            public void Execute(int index)
            {
                var blade = bladeData[index];
                float2 worldXZ = blade.positionAndFacingAngle.xz;
                float2 chunkLocalXZ = worldXZ - chunkWorldOrigin.xz;

                VoronoiGenerator.FindNearestCell(chunkLocalXZ, cells, out int nearestCellIndex, out float edgeDistance);

                var cell = cells[nearestCellIndex];

                float t= math.saturate(edgeDistance / edgeFalloffWidth);
                float edgeFalloff = t * t * (3f - 2f * t);   // The Smoothstep Function
                blade.voronoiCellID = (uint)nearestCellIndex;

                blade.height *= cell.heightVariation * math.lerp(0.6f,1.0f, edgeFalloff);


                // Width: wider in dense clusters
                // Remap density [0.5, 1.5] to a [0.6, 1.0] width multiplier
                blade.width *= math.lerp(0.6f, 1.0f, (cell.density - 0.5f));

                float cellColourOffset = (float)nearestCellIndex / (float)cells.Length;
                blade.colorVariationSeed = math.frac(blade.colorVariationSeed + cellColourOffset*0.4f);

                blade.windPhaseOffset = math.frac(blade.windPhaseOffset + nearestCellIndex * 0.37f);

                bladeData[index] = blade;
            }
        }

    }
}
