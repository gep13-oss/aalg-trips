using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Covers <see cref="GeoJsonRoute.TryParse"/>: it reads the ordered line out of a
    /// GeoJSON Feature, FeatureCollection or bare geometry, flips GeoJSON's
    /// [longitude, latitude] order to the site's [latitude, longitude], flattens a
    /// MultiLineString, and rejects malformed, out-of-range or too-short input.
    /// </summary>
    [TestFixture]
    public class GeoJsonRouteTests
    {
        [Test]
        public void A_feature_line_string_is_read_and_the_coordinate_order_is_flipped()
        {
            // GeoJSON positions are [longitude, latitude]; the site wants [lat, long].
            const string json = @"{
                ""type"": ""Feature"",
                ""geometry"": { ""type"": ""LineString"", ""coordinates"": [ [12.50, 41.90], [14.27, 40.85] ] }
            }";

            bool ok = GeoJsonRoute.TryParse(json, out var points);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(points, Has.Count.EqualTo(2));
                Assert.That(points[0][0], Is.EqualTo(41.90).Within(1e-9), "first latitude");
                Assert.That(points[0][1], Is.EqualTo(12.50).Within(1e-9), "first longitude");
                Assert.That(points[1][0], Is.EqualTo(40.85).Within(1e-9), "second latitude");
                Assert.That(points[1][1], Is.EqualTo(14.27).Within(1e-9), "second longitude");
            });
        }

        [Test]
        public void A_feature_collection_and_a_bare_geometry_are_both_accepted()
        {
            const string collection = @"{
                ""type"": ""FeatureCollection"",
                ""features"": [ { ""type"": ""Feature"", ""geometry"": { ""type"": ""LineString"", ""coordinates"": [ [0, 0], [1, 1] ] } } ]
            }";
            const string bare = @"{ ""type"": ""LineString"", ""coordinates"": [ [0, 0], [1, 1] ] }";

            Assert.Multiple(() =>
            {
                Assert.That(GeoJsonRoute.TryParse(collection, out var fromCollection), Is.True);
                Assert.That(fromCollection, Has.Count.EqualTo(2));
                Assert.That(GeoJsonRoute.TryParse(bare, out var fromBare), Is.True);
                Assert.That(fromBare, Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void A_multi_line_string_is_flattened_in_order()
        {
            const string json = @"{
                ""type"": ""MultiLineString"",
                ""coordinates"": [ [ [0, 0], [1, 1] ], [ [2, 2], [3, 3] ] ]
            }";

            bool ok = GeoJsonRoute.TryParse(json, out var points);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(points, Has.Count.EqualTo(4));
                Assert.That(points[2][0], Is.EqualTo(2).Within(1e-9));
                Assert.That(points[3][1], Is.EqualTo(3).Within(1e-9));
            });
        }

        [Test]
        public void Malformed_or_non_line_geometry_is_rejected()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GeoJsonRoute.TryParse("not json", out _), Is.False, "unparseable text");
                Assert.That(GeoJsonRoute.TryParse(string.Empty, out _), Is.False, "empty");
                Assert.That(GeoJsonRoute.TryParse(@"{ ""type"": ""Point"", ""coordinates"": [1, 2] }", out _), Is.False, "not a line");
            });
        }

        [Test]
        public void A_line_with_fewer_than_two_valid_points_is_rejected()
        {
            // One in-range point and one with an impossible latitude: the bad point is
            // dropped, leaving a single point, which is not a route.
            const string json = @"{ ""type"": ""LineString"", ""coordinates"": [ [12.5, 41.9], [12.6, 200.0] ] }";

            Assert.That(GeoJsonRoute.TryParse(json, out var points), Is.False);
            Assert.That(points, Has.Count.EqualTo(1));
        }
    }
}