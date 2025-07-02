using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace GhostOfTsushima.Runtime
{
    public class TerrainDisplacement : MonoBehaviour
    {
        [SerializeField] private ComputeShader displacementShader;
        private static readonly int VertBufferHash = Shader.PropertyToID("_Vertices");
        private static readonly int UVBufferHash = Shader.PropertyToID("_UVs");
        private static readonly int HeightMap = Shader.PropertyToID("_HeightMap");
        private static readonly int DisplacementStrength = Shader.PropertyToID("_DisplacementStrength");

        [SerializeField] private Texture2D heightMap;
        [SerializeField] private float displacementValue;

        private Mesh mesh;
        private MeshFilter _mf;

        private void Start()
        {
            _mf = GetComponent<MeshFilter>();
            mesh = GetComponent<MeshFilter>().mesh;
            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            
            ComputeBuffer vertexBuffer = new ComputeBuffer(verts.Length, sizeof(float) * 3);
            ComputeBuffer uvBuffer = new ComputeBuffer(uvs.Length, sizeof(float) * 2);
            vertexBuffer.SetData(verts);
            uvBuffer.SetData(uvs);
            
            displacementShader.SetBuffer(0, VertBufferHash, vertexBuffer);
            displacementShader.SetBuffer(0, UVBufferHash, uvBuffer);
            
            displacementShader.SetTexture(0, HeightMap, heightMap);
            displacementShader.SetFloat(DisplacementStrength, displacementValue);
            
            displacementShader.Dispatch(0, Mathf.CeilToInt(verts.Length/128.0f), 
                1, 1);
            
            vertexBuffer.GetData(verts); 
            vertexBuffer.Release();
            uvBuffer.Release();
            
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            MeshFilter mf = GetComponent<MeshFilter>();
            mf.sharedMesh = null;
            mf.sharedMesh = mesh;
        }

        private void Update()
        {
            Vector3[] verts = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            
            ComputeBuffer vertexBuffer = new ComputeBuffer(verts.Length, sizeof(float) * 3);
            ComputeBuffer uvBuffer = new ComputeBuffer(uvs.Length, sizeof(float) * 2);
            vertexBuffer.SetData(verts);
            uvBuffer.SetData(uvs);
            
            displacementShader.SetBuffer(0, VertBufferHash, vertexBuffer);
            displacementShader.SetBuffer(0, UVBufferHash, uvBuffer);
            
            displacementShader.SetTexture(0, HeightMap, heightMap);
            displacementShader.SetFloat(DisplacementStrength, displacementValue);
            
            displacementShader.Dispatch(0, Mathf.CeilToInt(verts.Length/128.0f), 
                1, 1);
            
            vertexBuffer.GetData(verts); 
            vertexBuffer.Release();
            uvBuffer.Release();
            
            mesh.vertices = verts;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _mf.sharedMesh = null;
            _mf.sharedMesh = mesh;
        }
    }
}