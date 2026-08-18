using RouteJumper.Models;
using Xunit;

namespace RouteJumper.Tests.Models
{
    public class GalacticCoordinatesTests
    {
        [Fact]
        public void DistanceTo_SamePoint_IsZero()
        {
            var point = new GalacticCoordinates(1, 2, 3);
            Assert.Equal(0, point.DistanceTo(point));
        }

        [Fact]
        public void DistanceTo_ThreeFourFiveTriangle_IsFive()
        {
            var a = new GalacticCoordinates(0, 0, 0);
            var b = new GalacticCoordinates(3, 4, 0);

            Assert.Equal(5.0, a.DistanceTo(b), precision: 6);
        }

        [Fact]
        public void DistanceTo_IsSymmetric()
        {
            var a = new GalacticCoordinates(1, 2, 3);
            var b = new GalacticCoordinates(-4, 5, -6);

            Assert.Equal(a.DistanceTo(b), b.DistanceTo(a), precision: 9);
        }
    }
}
