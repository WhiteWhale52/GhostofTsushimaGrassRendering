using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using UnityEngine.XR;

namespace GhostOfTsushima.Runtime
{
    public static class GrassInstancePopulator
    {

        public static void Populate(GrassChunk chunk, BatchMeshID batchMeshID, BatchMaterialID batchMaterialID, BatchRendererGroup batchRendererGroup, int instanceCount = 1000, TerrainMapData terrainData = null)
        {
            chunk.instanceCount = instanceCount;

            var bladeData = new NativeArray<GrassBladeInstanceData>(instanceCount, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            var job = new PopulateBladeDataJob
            {
                chunkOrigin = new float3(chunk.coordinate.x * 50f, 0, chunk.coordinate.z * 50f),
                bladeInstances = bladeData,
                seed = (uint)(chunk.coordinate.x * 73 + chunk.coordinate.z * 31 + 1),
                chunkSize = 50f
            };

            job.Schedule(instanceCount, 64).Complete();

            // Create a GraphicsBuffer for the instance data
            int instanceStride = UnsafeUtility.SizeOf<GrassBladeInstanceData>();
            int totalInts = (GrassConstants.BRGZeroBlockBytes + instanceStride * instanceCount) / sizeof(int);

            chunk.graphicsBufferInstanceData = new GraphicsBuffer(GraphicsBuffer.Target.Raw, totalInts, sizeof(int));

            var zeroBlock = new NativeArray<float4>(4, Allocator.Temp);
            chunk.graphicsBufferInstanceData.SetData(zeroBlock, 0, 0, 4);
            zeroBlock.Dispose();

            // Upload the instance data to the GraphicsBuffer
            int destStartIndex = GrassConstants.BRGZeroBlockBytes / instanceStride;
            chunk.graphicsBufferInstanceData.SetData(bladeData, 0, destStartIndex, instanceCount);

            bladeData.Dispose();

            // Set up metadata values to point to the instance data
            uint byteOffset = (uint)GrassConstants.BRGZeroBlockBytes;
            uint perInstance = 0x80000000;

            var metadata = new NativeArray<MetadataValue>(4, Allocator.Temp);
            metadata[0] = new MetadataValue
            {
                NameID = Shader.PropertyToID("_PositionAndAngle"),
                Value = perInstance | byteOffset
            };
            byteOffset += 16; // float4 = 16 bytes
            metadata[1] = new MetadataValue
            {
                NameID = Shader.PropertyToID("_HeightWidth"),
                Value = perInstance | byteOffset
            };
            byteOffset += 8; // float2 = 8 bytes
            metadata[2] = new MetadataValue
            {
                NameID = Shader.PropertyToID("_WindParameters"),
                Value = perInstance | byteOffset
            };
            byteOffset += 8;
            metadata[3] = new MetadataValue
            {
                NameID = Shader.PropertyToID("_BladeHash"),
                Value = perInstance | byteOffset
            };

            chunk.m_BatchID = batchRendererGroup.AddBatch(metadata, chunk.graphicsBufferInstanceData.bufferHandle);
            metadata.Dispose();

            chunk.isGenerated = true;

        }
 

           

        //// Raw buffers are allocated in ints. This is a utility method that calculates
        //// the required number of ints for the data.
        //private int BufferCountForInstances(int bytesPerInstance, int numInstances, int extraBytes = 0)
        //{
        //    bytesPerInstance = (bytesPerInstance + sizeof(int) - 1) / sizeof(int) * sizeof(int);
        //    extraBytes = (extraBytes + sizeof(int) - 1) / sizeof(int) * sizeof(int);
        //    int totalBytes = bytesPerInstance * numInstances + extraBytes;
        //    return totalBytes / sizeof(int);
        //}

        [BurstCompile]
        private struct PopulateBladeDataJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<GrassBladeInstanceData> bladeInstances;

            public float3 chunkOrigin;
            public float chunkSize;
            public uint seed;

            public void Execute(int index)
            {
                var random = new Unity.Mathematics.Random(seed + (uint)(index * 1087));

                // Random position within chunk
                float3 randomPos = chunkOrigin + new float3(
                    random.NextFloat() * chunkSize,
                    0.0f,
                    random.NextFloat() * chunkSize
                );

                bladeInstances[index] = new GrassBladeInstanceData
                {
                    positionAndFacingAngle = new float4(randomPos, random.NextFloat(0f, math.PI * 2f)),

                    height = random.NextFloat(0.7f, 1.3f),
                    width = random.NextFloat(0.8f, 1.2f),
                    lean = random.NextFloat(-0.15f, 0.15f),
                    curvatureStrength = random.NextFloat(-0.3f, 0.3f),
                    bendDirection = 0f,
                    bendStrength = 0f,
                    shapeProfileID = random.NextInt(0, 4),
                    colorVariationSeed = random.NextFloat(),
                    bladeHash = random.NextFloat(),
                    stiffness = random.NextFloat(0.4f, 0.9f),
                    windStrength = random.NextFloat(0.8f, 1.2f),
                    windPhaseOffset = random.NextFloat(0f, math.PI * 2f),
                    voronoiCellID = 0,
                    padding = 0f
                };
                Debug.Log($"Blade {index} has position {randomPos}");
            }
        }

        public class TerrainMapData
        {
            public Color[] heightPixels;
            public Color[] humidityPixels;
            public Color[] fertilityPixels;

            public int texHeight;
            public int texWidth;
            public float terrainWidth;
            public float terrainDepth;
            public float terrainAmplitude;
        }
    }
}
