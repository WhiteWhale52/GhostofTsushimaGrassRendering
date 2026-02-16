using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using Unity.Burst;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

// Removed unused 'using' statements for clarity
namespace GhostOfTsushima.Runtime
{
    public partial class GrassBlades : MonoBehaviour
    {
        // These need to match the shader property names EXACTLY
        private static readonly int TiltID = Shader.PropertyToID("_Tilt");
        private static readonly int MidpointID = Shader.PropertyToID("_Midpoint");
        private static readonly int BendID = Shader.PropertyToID("_Bend");
        private static readonly int HeightID = Shader.PropertyToID("_Height");
        private static readonly int GrassPositions = Shader.PropertyToID("GrassPositions");
        private static readonly int Max = Shader.PropertyToID("BoundsMax");
        private static readonly int Min = Shader.PropertyToID("BoundsMin");
        private static readonly int InstanceData = Shader.PropertyToID("_InstanceData");
        private static readonly int NumOfGrassBlades = Shader.PropertyToID("numOfGrassBlades");
        private static readonly int GlobalSeed = Shader.PropertyToID("GlobalSeed");
        private static readonly int GrassColorTexture = Shader.PropertyToID("_GrassColorTexture");

        //private NativeArray<GrassBladeData> m_GrassBlades;

        private GrassChunkSystemManager chunkSystemManager;
        private int updateFrameCounter = 0;

        [SerializeField] private Mesh m_GrassMesh;
        [SerializeField] private Material m_GrassMaterial;
        [SerializeField] private int numOfGrassBlades = 5000;
        
        [SerializeField, Min(0)] private int smallNumberOfGrassBlades = 30;
        [SerializeField] private Vector3 BoundsMin = new Vector3(0,0,0);
        [SerializeField] private Vector3 BoundsMax = new Vector3(5,0,5);

        private BatchRendererGroup m_BRG;
        
        //private GraphicsBuffer m_InstanceData;
        private BatchID m_BatchID;
        private BatchMeshID m_MeshID;
        private BatchMaterialID m_MaterialID;
        
        // private ComputeBuffer m_ComputeBuffer;
        // [SerializeField] private ComputeShader m_ComputeShader;
        [Required]
        public Texture2D grassBladesTexture;
        
        private int kSizeOfFloat4 = sizeof(float) * 4;
        private const int kSizeOfMatrix = sizeof(float) * 4 * 4;
        private const int kSizeOfPackedMatrix = sizeof(float) * 4 * 3;
        private const int kBytesPerInstance = (kSizeOfPackedMatrix * 2);


        [SerializeField] private int chunkUpdateFrames = 10;

        public int jobBatchSize;

        
        private void OnEnable()
        {
            // Creating the High LOD Grass Blade in CPU
            m_GrassMesh = CreateHighLODGrassBladesMesh();
           // m_GrassMaterial.EnableKeyword("DOTS_INSTANCING_ON");
            // Initialized the BRG
            m_BRG = new BatchRendererGroup(this.OnPerformCulling, IntPtr.Zero);
            // Initialized and Registered the mesh and material to BRG instance
            if (m_GrassMesh) m_MeshID = m_BRG.RegisterMesh(m_GrassMesh);
            if (m_GrassMaterial) m_MaterialID = m_BRG.RegisterMaterial(m_GrassMaterial);
            
            
        

            //TODO: Create a Compute Shader 
            // The compute shader will create numOfGrassBlades instances of GrassBladeData
            // And for each will populate the instances with their data
            // The compute shader should also use a Displacement buffer and wind texture to 
            /// Like this:
            // Declare the fields
            // private ComputeBuffer m_InstanceBuffer;
            // private ComputeShader m_InstanceDataCompute;
            // In OnEnable
            // m_InstanceBuffer = new ComputeShader(numOfGrassBlades, Marshal.SizeOf<InstanceData>())
            // In Compute Shader
            // Declare the kernel name
            // #pragma kernel ComputeGrassData
            // RWStructuredBuffer<GrassBladeData> m_InstanceBuffer;
            // [numthreads(128,21,1)]
            // void ComputeGrassData(uint3 id : SV_DispatchThreadID)
            //{
            // uint index = id.x + id.y * 16 + id.z * 256;
            // if (index >= m_InstanceBuffer.Length) return;
            // TODO: Write code here that populates the buffer
            // The blade positions should be affected by the terrain below it


            // Then in C# script again
            // private static readonly int InstanceBufferID = Shader.PropertyToID("m_InstanceBuffer");
            // Assign m_InstanceDataCompute = Resources.Load<ComputeShader>"GrassDataCompute";
            // int kernel = m_InstanceDataCompute.FindKernel("ComputeGrassData");
            // m_InstanceDataCompute.SetBuffer(kernel, InstanceBufferID, m_InstanceBuffer);
            // TODO: Determine the thread count and then dispatch shader

            // Place one zero matrix at the start of the instance data buffer, so loads from address 0 will return zero

            // TODO: Loop over all the instances in m_InstanceBuffer and 
            // make a PackedMatrix[numOfGrassBlades] {} for each of them

            // TODO: Setup metadata, like this  
            //// var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
            // metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = 0x80000000 | byteAddressObjectToWorld, };
            // metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = 0x80000000 | byteAddressWorldToObject, };
            // metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"), Value = 0x80000000 | byteAddressColor, };
            // TODO: Finally add the batch to m_BRG
        }

       

        private void Start()
        {
			chunkSystemManager = new GrassChunkSystemManager(
			m_BRG,
			m_GrassMesh,
			m_GrassMaterial,
			Camera.main.transform		 
            );

            chunkSystemManager.UpdateVisibleChunks();

        }



		private void Update()
		{
			updateFrameCounter++;

			// Update chunks every chunkUpdateFrames frames
			if (updateFrameCounter % chunkUpdateFrames == 0)
			{
				chunkSystemManager?.UpdateVisibleChunks();
			}
		}

		Mesh CreateHighLODGrassBladesMesh()
        {

			Mesh mesh = new Mesh();

			int segments = 7; // Start with 7 for testing
			List<Vector3> vertices = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<int> triangles = new List<int>();

			// Build blade as a ribbon
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;

				// Left vertex
				vertices.Add(Vector3.zero); // Position computed in shader
				uvs.Add(new Vector2(0.0f, t)); // UV encodes: left side, height t

				// Right vertex
				vertices.Add(Vector3.zero); // Position computed in shader
				uvs.Add(new Vector2(1.0f, t)); // UV encodes: right side, height t
			}

			// Build triangles
			for (int i = 0; i < segments; i++)
			{
				int baseIdx = i * 2;

				// Triangle 1
				triangles.Add(baseIdx + 0);
				triangles.Add(baseIdx + 1);
				triangles.Add(baseIdx + 2);

				// Triangle 2
				triangles.Add(baseIdx + 1);
				triangles.Add(baseIdx + 3);
				triangles.Add(baseIdx + 2);
			}

			mesh.SetVertices(vertices);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(triangles, 0);

			// DON'T call RecalculateNormals - shader computes them
			mesh.RecalculateBounds();

			return mesh;
		}
        

        
   //     PopulateInstanceDataBuffer
   //     [BurstCompile]
   //     private void PopulateInstanceDataBuffer()
   //     {

			//var bladeParameters = new NativeArray<GrassBladeInstanceData>(numOfGrassBlades, Allocator.Temp);
            
   //         PopulateBladeParametersJob job = new PopulateBladeParametersJob
   //         {
   //                 bladeInstances = bladeParameters,
   //                 boundsMin = BoundsMin,
   //                 boundsMax = BoundsMax,
   //                 rand = new Unity.Mathematics.Random((uint)Time.realtimeSinceStartup * 43)
   //         };
   //         JobHandle handle = job.Schedule(numOfGrassBlades, jobBatchSize);
   //         handle.Complete();
            

   //         // In this simple example, the instance data is placed into the buffer like this:
   //         // Offset | Description
   //         //      0 | 64 bytes of zeroes, so loads from address 0 return zeroes
   //         //     64 | 32 uninitialized bytes to make working with SetData easier, otherwise unnecessary
   //         //     96 | unity_ObjectToWorld, three packed float3x4 matrices
   //         //    240 | unity_WorldToObject, three packed float3x4 matrices

   //         // Calculates start addresses for the different instanced properties. unity_ObjectToWorld starts
   //         // at address 96 instead of 64, because the computeBufferStartIndex parameter of SetData
   //         // is expressed as source array elements, so it is easier to work in multiples of sizeof(PackedMatrix).
   //         int sizeOfBladeData = UnsafeUtility.SizeOf<GrassBladeInstanceData>();
   //         uint byteAddressBladeData = 64;
   //    //    uint byteAddressColor = byteAddressWorldToObject + kSizeOfPackedMatrix * (uint) smallNumberOfGrassBlades;

   //         // Upload the instance data to the GraphicsBuffer so the shader can load them.
   //         var zero = new float4x4[1] { float4x4.zero };
   //         m_InstanceData.SetData(zero, 0, 0, 1);
   //         m_InstanceData.SetData(bladeParameters, 0, 1, bladeParameters.Length);

   //         // Set up metadata values to point to the instance data. Set the most significant bit 0x80000000 in each
   //         // which instructs the shader that the data is an array with one value per instance, indexed by the instance index.
   //         // Any metadata values that the shader uses that are not set here will be 0. When a value of 0 is used with
   //         // UNITY_ACCESS_DOTS_INSTANCED_PROP (i.e. without a default), the shader interprets the
   //         // 0x00000000 metadata value and loads from the start of the buffer. The start of the buffer is
   //         // a zero matrix so this sort of load is guaranteed to return zero, which is a reasonable default value.
   //         var metadata = new NativeArray<MetadataValue>(10, Allocator.Temp);
			//int offset = 64; // Start after zero block

			//metadata[0] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Position"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 12; // float3 = 12 bytes (but aligned to 16)
			//offset = (offset + 15) & ~15; // Round up to next 16-byte boundary

			//metadata[1] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_FacingAngle"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[2] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Height"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[3] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Width"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[4] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Curvature"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[5] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Lean"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[6] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_ColorSeed"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[7] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_BladeHash"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[8] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_Stiffness"),
			//	Value = 0x80000000 | (uint)offset
			//};
			//offset += 4;

			//metadata[9] = new MetadataValue
			//{
			//	NameID = Shader.PropertyToID("_WindPhaseOffset"),
			//	Value = 0x80000000 | (uint)offset
			//};

   //         // Finally, create a batch for the instances and make the batch use the GraphicsBuffer with the
   //         // instance data as well as the metadata values that specify where the properties are.
   //         m_BatchID = m_BRG.AddBatch(metadata, m_InstanceData.bufferHandle);

   //         bladeParameters.Dispose();
   //         metadata.Dispose();

   //     }


        [BurstCompile]
        private unsafe JobHandle OnPerformCulling(BatchRendererGroup rendererGroup, 
        BatchCullingContext cullingContext, BatchCullingOutput cullingOutput, IntPtr userContext)
        {
                // UnsafeUtility.Malloc() requires an alignment, so use the largest integer type's alignment
             // which is a reasonable default.
             int alignment = UnsafeUtility.AlignOf<long>();

             // Acquire a pointer to the BatchCullingOutputDrawCommands struct so you can easily
             // modify it directly.
             var drawCommands = (BatchCullingOutputDrawCommands*)cullingOutput.drawCommands.GetUnsafePtr();

             // Allocate memory for the output arrays. In a more complicated implementation, you would calculate
             // the amount of memory to allocate dynamically based on what is visible.
             // This example assumes that all of the instances are visible and thus allocates
             // memory for each of them. The necessary allocations are as follows:
             // - a single draw command (which draws kNumInstances instances)
             // - a single draw range (which covers our single draw command)
             // - kNumInstances visible instance indices.
             // You must always allocate the arrays using Allocator.TempJob.
             drawCommands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>(), alignment, Allocator.TempJob);
             drawCommands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
             drawCommands->visibleInstances = (int*)UnsafeUtility.Malloc(numOfGrassBlades * sizeof(int), alignment, Allocator.TempJob);
             drawCommands->drawCommandPickingEntityIds = null;

             drawCommands->drawCommandCount = 1;
             drawCommands->drawRangeCount = 1;
             drawCommands->visibleInstanceCount = (int)numOfGrassBlades;

             // This example doens't use depth sorting, so it leaves instanceSortingPositions as null.
             drawCommands->instanceSortingPositions = null;
             drawCommands->instanceSortingPositionFloatCount = 0;

             // Configure the single draw command to draw kNumInstances instances
             // starting from offset 0 in the array, using the batch, material and mesh
             // IDs registered in the Start() method. It doesn't set any special flags.
             drawCommands->drawCommands[0].visibleOffset = 0;
             drawCommands->drawCommands[0].visibleCount = (uint) numOfGrassBlades;
             foreach (GrassChunk chunk in chunkSystemManager.GetActiveChunks().Values){ 
                 drawCommands->drawCommands[0].batchID = chunk.m_BatchID;
                 drawCommands->drawCommands[0].materialID = m_MaterialID;
                 drawCommands->drawCommands[0].meshID = m_MeshID;
             }
             drawCommands->drawCommands[0].submeshIndex = 0;
             drawCommands->drawCommands[0].splitVisibilityMask = 0xff;
             drawCommands->drawCommands[0].flags = 0;
             drawCommands->drawCommands[0].sortingPosition = 0;

             // Configure the single draw range to cover the single draw command which
             // is at offset 0.
             drawCommands->drawRanges[0].drawCommandsType = BatchDrawCommandType.Direct;
             drawCommands->drawRanges[0].drawCommandsBegin = 0;
             drawCommands->drawRanges[0].drawCommandsCount = 1;

             // This example doesn't care about shadows or motion vectors, so it leaves everything
             // at the default zero values, except the renderingLayerMask which it sets to all ones
             // so Unity renders the instances regardless of mask settings.
             drawCommands->drawRanges[0].filterSettings = new BatchFilterSettings { renderingLayerMask = 0xffffffff, };

             // Finally, write the actual visible instance indices to the array. In a more complicated
             // implementation, this output would depend on what is visible, but this example
             // assumes that everything is visible.
             for (int i = 0; i < numOfGrassBlades; ++i)
                 drawCommands->visibleInstances[i] = i;

             // This simple example doesn't use jobs, so it returns an empty JobHandle.
             // Performance-sensitive applications are encouraged to use Burst jobs to implement
             // culling and draw command output. In this case, this function returns a
             // handle here that completes when the Burst jobs finish.
            return new JobHandle();
        }
        
        // Raw buffers are allocated in ints. This is a utility method that calculates
        // the required number of ints for the data.
        int BufferCountForInstances(int bytesPerInstance, int numInstances, int extraBytes = 0)
        {
            // Round byte counts to int multiples
            bytesPerInstance = (bytesPerInstance + sizeof(int) - 1) / sizeof(int) * sizeof(int);
            extraBytes = (extraBytes + sizeof(int) - 1) / sizeof(int) * sizeof(int);
            int totalBytes = bytesPerInstance * numInstances + extraBytes;
            return totalBytes / sizeof(int);
        }

        // Helper function to allocate BRG buffers during the BRG callback function
        private static unsafe T* Malloc<T>(uint count) where T : unmanaged
        {
            return (T*)UnsafeUtility.Malloc(
                UnsafeUtility.SizeOf<T>() * count,
                UnsafeUtility.AlignOf<T>(),
                Allocator.TempJob);
        }

        [BurstCompile]
        private struct PopulateBladeParametersJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<GrassBladeInstanceData> bladeInstances;    

            public float3 boundsMin;
            public float3 boundsMax;
            public Unity.Mathematics.Random rand;
            
            public void Execute(int index)
            {
                float3 size = boundsMax - boundsMin;

                float3 randomPos = boundsMin + new float3(
                rand.NextFloat() * size.x,
                0.0f,
                rand.NextFloat() * size.z);

                bladeInstances[index] = new GrassBladeInstanceData
                {
                    position = randomPos,
                    facingAngle = rand.NextFloat() * 2.0f * math.PI,

                    height = rand.NextFloat(0.5f, 1.5f),
                    width = rand.NextFloat(0.02f, 0.04f),
                    curvatureStrength = rand.NextFloat(-0.4f, 0.4f),
                    lean = rand.NextFloat(-0.2f, 0.2f),

                    shapeProfileID = rand.NextInt(0, 8),
                    colorVariationSeed = rand.NextFloat(),
                    bladeHash = rand.NextFloat(),
                    stiffness = rand.NextFloat(0.3f, 0.8f),

                    windPhaseOffset = rand.NextFloat(0, 2f * math.PI),
                    windStrength = rand.NextFloat(0.8f, 1.2f),
                };

            }
        }
		private void OnDisable()
		{
            chunkSystemManager?.Dispose();
			m_BRG.Dispose();
			//m_InstanceData.Release();
		}

	}
     
}