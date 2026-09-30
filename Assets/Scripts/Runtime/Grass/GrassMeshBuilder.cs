using UnityEngine;
using System.Collections.Generic;

namespace GhostOfTsushima.Runtime
{
    public static class GrassMeshBuilder
    {
        public static Mesh BuildBladeMesh(int segments)
        {
            Mesh mesh = new Mesh();

            var vertices =new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>(); // Two triangles per segment

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;

                // Add two vertices for each segment (left and right)
                vertices.Add(Vector3.zero); // Root vertex
                uvs.Add(new Vector2(GrassConstants.UVLeft, t));

                vertices.Add(Vector3.zero);
                uvs.Add(new Vector2(GrassConstants.UVRight, t));
            }

            for (int i = 0; i < segments; i++)
            {
                int baseIndex = i * 2;

                // Triangle 1 (lower-left → upper-left → lower-right)
                triangles.Add(baseIndex);
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 2);
                // Triangle 2 (upper-left → upper-right → lower-right)
                triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex + 3);
                triangles.Add(baseIndex + 2);
            }
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);

            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1, 3f, 1));
            return mesh;
        }


        public static Mesh BuildBladeMeshLOD(int lodLevel)
        {
            return lodLevel switch
            {
                0 => BuildBladeMesh(15), // Highest detail
                1 => BuildBladeMesh(7), // Medium detail
                2 => BuildBladeMesh(4),// Low detail
                3 => BuildBladeMesh(2),// Very low detail
                _ => BuildBladeMesh(7)// Default to medium detail if an invalid LOD level is provided
            };
        }
    }
}
