using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Coverage for the great-circle distance helper the castle list is ordered by:
    /// a point is zero miles from itself, a known city pair comes out close to its
    /// real straight-line distance, and the result does not depend on argument order.
    /// </summary>
    [TestFixture]
    public class GeoDistanceTests
    {
        [Test]
        public void A_point_is_zero_miles_from_itself()
        {
            Assert.That(GeoDistance.Miles(57.3626, -2.0817, 57.3626, -2.0817), Is.EqualTo(0).Within(1e-6));
        }

        [Test]
        public void A_known_pair_matches_its_real_straight_line_distance()
        {
            // London (51.5074, -0.1278) to Edinburgh (55.9533, -3.1883) is ~331 miles.
            var miles = GeoDistance.Miles(51.5074, -0.1278, 55.9533, -3.1883);

            Assert.That(miles, Is.EqualTo(331).Within(8));
        }

        [Test]
        public void Distance_is_symmetric()
        {
            var there = GeoDistance.Miles(57.3626, -2.0817, 55.9533, -3.1883);
            var back = GeoDistance.Miles(55.9533, -3.1883, 57.3626, -2.0817);

            Assert.That(there, Is.EqualTo(back).Within(1e-9));
        }
    }
}