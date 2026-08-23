using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Covers <see cref="GeoJsonRoute.TryParse"/> and the stored-route reader
    /// <see cref="RouteSegment.FromStoredJson"/>: parsing a Feature, FeatureCollection
    /// or bare geometry into styled segments, flipping GeoJSON's [longitude, latitude]
    /// order, marking a <c>travel</c> feature as a dashed segment, flattening a
    /// MultiLineString, rejecting malformed input, and reading an older flat route file
    /// back as one solid segment.
    /// </summary>
    [TestFixture]
    public class GeoJsonRouteTests
    {
        [Test]
        public void A_feature_line_string_is_one_solid_segment_with_flipped_coordinates()
        {
            // GeoJSON positions are [longitude, latitude]; the site wants [lat, long].
            const string json = @"{
                ""type"": ""Feature"",
                ""geometry"": { ""type"": ""LineString"", ""coordinates"": [ [12.50, 41.90], [14.27, 40.85] ] }
            }";

            bool ok = GeoJsonRoute.TryParse(json, out var segments);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(segments, Has.Count.EqualTo(1));
                Assert.That(segments[0].Travel, Is.False);
                Assert.That(segments[0].Points, Has.Count.EqualTo(2));
                Assert.That(segments[0].Points[0][0], Is.EqualTo(41.90).Within(1e-9), "first latitude");
                Assert.That(segments[0].Points[0][1], Is.EqualTo(12.50).Within(1e-9), "first longitude");
                Assert.That(segments[0].Points[1][0], Is.EqualTo(40.85).Within(1e-9), "second latitude");
            });
        }

        [Test]
        public void A_feature_collection_marks_a_travel_feature_as_a_dashed_segment()
        {
            // A solid covered track plus a dashed travel hop (a flight) — the second
            // feature carries properties.travel = true.
            const string json = @"{
                ""type"": ""FeatureCollection"",
                ""features"": [
                    { ""type"": ""Feature"", ""geometry"": { ""type"": ""LineString"", ""coordinates"": [ [12.5, 41.9], [14.27, 40.85] ] } },
                    { ""type"": ""Feature"", ""properties"": { ""travel"": true }, ""geometry"": { ""type"": ""LineString"", ""coordinates"": [ [14.27, 40.85], [108.94, 34.26] ] } }
                ]
            }";

            bool ok = GeoJsonRoute.TryParse(json, out var segments);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(segments, Has.Count.EqualTo(2));
                Assert.That(segments[0].Travel, Is.False, "the covered track is solid");
                Assert.That(segments[1].Travel, Is.True, "the flagged feature is a dashed travel hop");
                Assert.That(segments[1].Points[1][0], Is.EqualTo(34.26).Within(1e-9));
                Assert.That(segments[1].Points[1][1], Is.EqualTo(108.94).Within(1e-9));
            });
        }

        [Test]
        public void A_multi_line_string_becomes_one_solid_segment_per_line()
        {
            const string json = @"{
                ""type"": ""MultiLineString"",
                ""coordinates"": [ [ [0, 0], [1, 1] ], [ [2, 2], [3, 3] ] ]
            }";

            bool ok = GeoJsonRoute.TryParse(json, out var segments);

            Assert.Multiple(() =>
            {
                Assert.That(ok, Is.True);
                Assert.That(segments, Has.Count.EqualTo(2));
                Assert.That(segments.TrueForAll(s => !s.Travel), Is.True);
                Assert.That(segments[1].Points[0][0], Is.EqualTo(2).Within(1e-9));
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
            // dropped, leaving a single point, which is not a line, so the whole parse
            // fails.
            const string json = @"{ ""type"": ""LineString"", ""coordinates"": [ [12.5, 41.9], [12.6, 200.0] ] }";

            Assert.That(GeoJsonRoute.TryParse(json, out _), Is.False);
        }

        [Test]
        public void An_older_flat_route_file_reads_as_one_solid_segment()
        {
            // A route uploaded before segments was stored as a flat [lat, lng] array;
            // it must still read back as one solid segment (no migration).
            const string flat = @"[ [41.90, 12.50], [40.85, 14.27] ]";

            var segments = RouteSegment.FromStoredJson(flat);

            Assert.Multiple(() =>
            {
                Assert.That(segments, Is.Not.Null);
                Assert.That(segments, Has.Count.EqualTo(1));
                Assert.That(segments[0].Travel, Is.False);
                Assert.That(segments[0].Points, Has.Count.EqualTo(2));
                Assert.That(segments[0].Points[0][0], Is.EqualTo(41.90).Within(1e-9));
            });
        }
    }
}