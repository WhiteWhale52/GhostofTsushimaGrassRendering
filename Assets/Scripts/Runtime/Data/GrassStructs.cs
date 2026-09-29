using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace GhostOfTsushima.Runtime
{
    [StructLayout(LayoutKind.Sequential)]
    public struct GrassBladeInstanceData
    {
        // Instance data for each grass blade
        public float4 positionAndFacingAngle;

        public float height;
        public float width;
        public float lean;
        public float curvatureStrength;


        //TODO: Replace with the bezier curve control points 
        public float bendDirection;
        public float bendStrength;


        public int shapeProfileID;

        public float colorVariationSeed;
        public float bladeHash;
        public float stiffness;


        public float windStrength;
        public float windPhaseOffset;
        public uint voronoiCellID;
        public float padding;
    }
    // 16 + 4*6 + 4 + 4*3 + 4*2 + 4 + 4 = 16 + 24 + 4 + 12 + 8 + 4 + 4 = 72... 
    // Recalculate and adjust padding count to land on a multiple of 16.
    struct BladeVertex {
   
        public float3 position;
        public float3 normal;
        public float2 uv;

    }

    [StructLayout(LayoutKind.Sequential)]
    struct HermiteCurveParams
    {
        public float3 rootPosition;
        public float rootTangentX;

        public float3 tipPosition;
        public float rootTangentY;

    }

    public struct  VoronoiCell
    {
        public float2 centre;
        public float density;
        public float heightVariation;
        public float humidity;
        public float fertility;
    }

    public struct SoilData
    {
        public float humidity;
        public float fertility;
        public float temperature;
        public float compaction;
        public float lightExposure;
    }
}
