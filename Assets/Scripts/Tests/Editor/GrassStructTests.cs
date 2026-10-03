using Unity.Collections.LowLevel.Unsafe;
using NUnit.Framework;
using GhostOfTsushima.Runtime;

namespace GhostOfTsushima.Tests
{
  
    public class GrassStructTests
    {
        [Test]
        public void GrassBladeInstanceData_Is64Bytes()
        {
            int size = UnsafeUtility.SizeOf<GrassBladeInstanceData>();
            Assert.AreEqual(64, size,
                $"GrassBladeInstanceData is {size} bytes."+
                $"Every byte over or under will shift all the metadata offsets.");
        }

        [Test]
        public void HermiteCurveParams_Is32Bytes()
        {
            int size = UnsafeUtility.SizeOf<HermiteCurveParams>();
            Assert.AreEqual(32, size,
                $"HermiteCurveParams is {size} bytes." +
                $"The expected size is 32 bytes.");
        }

    }
}
