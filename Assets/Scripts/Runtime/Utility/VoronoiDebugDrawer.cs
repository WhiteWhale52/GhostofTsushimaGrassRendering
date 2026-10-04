#if UNITY_EDITOR
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace GhostOfTsushima.Runtime
{
    [ExecuteInEditMode]
    public class VoronoiDebugDrawer : MonoBehaviour
    {
        [Header("Voronoi Noise Settings")]
        [SerializeField] private int previewCellCount = 10;
        [SerializeField] private float previewChunkSize = 50f;
        [SerializeField] private int2 previewChunkCoord = int2.zero;
        [SerializeField] private uint previewGlobalSeed = 12345u;

        [Header("Draw Options")]
        [SerializeField] private bool drawCellCentres = true;
        [SerializeField] private bool drawCellLabels = true;
        [SerializeField] private bool drawEdgeSampling = false;
        [SerializeField] private int edgeSampleGrid = 20;

        private NativeArray<VoronoiCell> previewCells;

        [ContextMenu("Regenerate the Preview")]
        private void RegeneratePreview()
        {
            if (previewCells.IsCreated)
            {
                previewCells.Dispose();
            }
            previewCells = new NativeArray<VoronoiCell>(previewCellCount, Allocator.Persistent);
            VoronoiGenerator.GenerateCells(previewChunkCoord, previewChunkSize, previewCellCount, previewGlobalSeed, previewCells);
        }

        private void OnEnable()
        {
            RegeneratePreview();
        }

        private void OnDrawGizmosSelected()
        {
            if (!previewCells.IsCreated)
            {
                return;
            }

            float3 origin = new float3(previewChunkCoord.x * previewChunkSize, 0, previewChunkCoord.y * previewChunkSize);

            Gizmos.color = Color.white;

            var corners = new Vector3[]
            {
                ToWorld(origin, 0, 0),
                ToWorld(origin, previewChunkSize, 0),
                ToWorld(origin, previewChunkSize, previewChunkSize),
                ToWorld(origin, 0, previewChunkSize)
            };

            for (int i = 0; i < corners.Length -1; i++)
            {
                Gizmos.DrawLine(corners[i], corners[i + 1]);
            }
            if (drawCellCentres)
            {
                for (int i = 0; i < previewCells.Length; i++)
                {
                    var cell = previewCells[i];
                    float t = (cell.density - 0.5f)/1.0f;
                    Gizmos.color = Color.Lerp(Color.blue, Color.green, t);

                    var worldCentre = ToWorld(origin, cell.centre.x, cell.centre.y);

                    Gizmos.DrawSphere(worldCentre, 0.3f + cell.density * 0.3f);

                    Gizmos.color = Color.Lerp(Color.yellow, Color.cyan, cell.humidity);
                    Gizmos.DrawWireSphere(worldCentre,1.5f + cell.fertility);

                    if (drawCellLabels)
                    {
                        Handles.Label(worldCentre + Vector3.up * 0.5f,
                            $"ID: {i}\nD: {cell.density:F2}\nH: {cell.humidity:F2}\nF: {cell.fertility:F2}");
                    }
                }
            }
            if (drawEdgeSampling)
            {
                var cellColours = new Color[previewCells.Length];
                var random = new Unity.Mathematics.Random(previewGlobalSeed + 93u);
                for (int i = 0; i < cellColours.Length; i++)
                {
                    cellColours[i] = new Color(random.NextFloat(), random.NextFloat(), random.NextFloat());
                }

                float step = previewChunkSize / edgeSampleGrid;

                for (int zi = 0; zi <= edgeSampleGrid; zi++)
                {
                    for (int xi = 0; xi <= edgeSampleGrid; xi++)
                    {
                        float localX = xi * step;
                        float localZ = zi * step;
                        float2 samplePos = new float2(localX, localZ);
                        VoronoiGenerator.FindNearestCell(samplePos, previewCells, out int nearestCellIndex, out float edgeDistance);
                        float edgeT = math.saturate(edgeDistance / (previewChunkSize * VoronoiClusteringSystem.EdgeFalloffFraction));
                        edgeT = edgeT * edgeT * (3f - 2f * edgeT);   // smoothstep
                        Gizmos.color = Color.Lerp(Color.black, cellColours[nearestCellIndex], edgeT);
                        var worldPos = ToWorld(origin, localX, localZ);
                        Gizmos.DrawSphere(worldPos, step * 0.3f);
                    }
                }
            }
        }


       private static Vector3 ToWorld(float3 origin, float localX, float localZ) =>
          new Vector3(origin.x + localX, origin.y, origin.z + localZ);
        private void OnDisable()
        {
            if (previewCells.IsCreated)
            {
                previewCells.Dispose();
            }

        }
    }
}

#endif
