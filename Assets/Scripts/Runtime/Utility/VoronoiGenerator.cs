using Unity.Collections;
using Unity.Mathematics;


namespace GhostOfTsushima.Runtime
{
    public static class VoronoiGenerator
    {
        // Deterministic: same chunkCoord + globalSeed always gives the same cells.
        // Called once per chunk load on the main thread before any jobs run.
        public static void GenerateCells(int2 chunkCoord, float chunkSize, int cellCount, uint globalSeed, NativeArray<VoronoiCell> cells)
        {
            // Implementation for generating Voronoi cells
            uint chunkSeed = math.hash(new uint3((uint)(chunkCoord.x), (uint)(chunkCoord.y), globalSeed));

            var random = new Random(chunkSeed == 0 ? 1u : chunkSeed);

            for (int i = 0; i < cellCount; i++)
            {
                cells[i] = new VoronoiCell
                {
                    centre = random.NextFloat2(float2.zero, new float2(chunkSize, chunkSize)),
                    density = random.NextFloat(0.5f, 1.5f),
                    heightVariation = random.NextFloat(0.7f, 1.5f),
                    humidity = random.NextFloat(0.3f, 0.9f),
                    fertility = random.NextFloat(0.4f, 1f),
                };
            }
        }

        public static void FindNearestCell(float2 chunkLocalXZ, NativeArray<VoronoiCell> cells, out int nearestCellIndex, out float edgeDistance)
        {
            nearestCellIndex = 0;
            float minDist = float.MaxValue;
            float minDist2 = float.MaxValue;   

            for (int i = 0; i < cells.Length; i++)
            {
                // Use actual distance (not squared) because we need edgeDistance
                // in world units — squaring would make the subtraction meaningless
                float d = math.distancesq(chunkLocalXZ, cells[i].centre);

                if (d < minDist)
                {
                    minDist2 = minDist;
                    minDist = d;
                    nearestCellIndex = i;
                }
                else if (d < minDist2)
                {
                    minDist2 = d;
                }
            }

            // If only one cell exists, set edgeDistance to a large value
            edgeDistance = (minDist2 < float.MaxValue)
                ? math.sqrt(minDist2) - math.sqrt(minDist)
                : float.MaxValue;
        }

        public static SoilData AverageCellsToSoil(NativeArray<VoronoiCell> cells)
        {
            float humidity = 0;
            float fertility = 0;
            for (int i=0; i < cells.Length; i++)
            {
                humidity += cells[i].humidity;
                fertility += cells[i].fertility;
            }

            float inv = 1f / cells.Length;
            return new SoilData
            {
                humidity = humidity * inv,
                fertility = fertility * inv,
                lightExposure = 1f, // Default value, can be adjusted based on other factors
                compaction = 0f,
                temperature = 20f
            };
        }
    }
}
