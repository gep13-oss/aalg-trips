using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// End-to-end coverage for a journey's uploaded route: an admin uploads a GeoJSON
    /// line through the journey's "Upload route map" modal, the server parses it (a
    /// LineString, its [lon, lat] order flipped to [lat, long]) and stores it, and it
    /// surfaces as the journey's <c>Geometry</c> in the regenerated <c>journeys.json</c>
    /// the map reads. Removing the route through the actions menu drops the geometry
    /// again. The journey is created and deleted within the test so the suite stays
    /// self-contained.
    /// </summary>
    [TestFixture]
    public class JourneyRouteUploadTests : UITestBase
    {
        // A GeoJSON Feature LineString of three [longitude, latitude] positions.
        private static readonly byte[] RouteGeoJson = Encoding.UTF8.GetBytes(
            @"{""type"":""Feature"",""geometry"":{""type"":""LineString"",""coordinates"":[[12.5,41.9],[13.0,41.5],[14.27,40.85]]}}");

        [Test]
        public async Task Uploading_a_route_stores_its_geometry_and_removing_it_clears_it()
        {
            await SignInAsync();

            var title = "Route Journey " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                slug = await CreateJourneyAsync(title);

                // Upload a route through the actions-menu "Upload route map" modal.
                await Page.ClickAsync("summary.actions-menu__trigger");
                await Page.WaitForSelectorAsync(".actions-menu[open]");
                await Page.ClickAsync("[data-open-dialog='#uploadRouteDialog']");
                await Page.WaitForSelectorAsync("#uploadRouteDialog[open]");
                await Page.SetInputFilesAsync("#routeFile", new FilePayload
                {
                    Name = "route.geojson",
                    MimeType = "application/geo+json",
                    Buffer = RouteGeoJson,
                });
                await Page.ClickAsync("#btnRouteUpload");
                await Page.WaitForURLAsync(new Regex($"/journey/{slug}/$"));

                // The route now surfaces as the journey's geometry in the journeys file:
                // one solid segment (the uploaded LineString) whose points carry the
                // GeoJSON [lon, lat] order flipped to the site's [lat, long].
                var geometry = await JourneyGeometryAsync(slug);
                Assert.That(geometry, Is.Not.Null);
                Assert.That(geometry!.Value.GetArrayLength(), Is.EqualTo(1), "one segment");

                var segment = geometry.Value[0];
                var points = segment.GetProperty("Points");
                Assert.Multiple(() =>
                {
                    Assert.That(segment.GetProperty("Travel").GetBoolean(), Is.False, "an uploaded track is solid");
                    Assert.That(points.GetArrayLength(), Is.EqualTo(3));
                    Assert.That(points[0][0].GetDouble(), Is.EqualTo(41.9).Within(1e-9), "first latitude");
                    Assert.That(points[0][1].GetDouble(), Is.EqualTo(12.5).Within(1e-9), "first longitude");
                });

                // Remove the route through the actions menu; the geometry is dropped.
                await Page.ClickAsync("summary.actions-menu__trigger");
                await Page.WaitForSelectorAsync(".actions-menu[open]");
                await Page.ClickAsync("#deleteroute");
                await Page.WaitForURLAsync(new Regex($"/journey/{slug}/$"));

                var cleared = await JourneyGeometryAsync(slug);
                Assert.That(cleared, Is.Null, "the route geometry is gone after removal");
            }
            finally
            {
                if (slug != null)
                {
                    await DeleteJourneyAsync(slug);
                }
            }
        }

        // Reads the given journey's Geometry out of the live journeys.json, or null when
        // the journey has no route (the property is serialized as JSON null).
        private async Task<JsonElement?> JourneyGeometryAsync(string slug)
        {
            var response = await Page.APIRequest.GetAsync(BaseUrl + "/albums/journeys.json");
            Assert.That(response.Ok, Is.True, "journeys.json should be served");

            var body = await response.TextAsync();
            using var document = JsonDocument.Parse(body);

            foreach (var journey in document.RootElement.EnumerateArray())
            {
                if (journey.GetProperty("Slug").GetString() == slug)
                {
                    var geometry = journey.GetProperty("Geometry");
                    return geometry.ValueKind == JsonValueKind.Null ? null : geometry.Clone();
                }
            }

            return null;
        }
    }
}