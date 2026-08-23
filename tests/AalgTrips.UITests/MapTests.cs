using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Covers the Leaflet + OpenStreetMap album map that replaced Google Maps.
    /// The map lives on the home page, which now requires authentication, so each
    /// test signs in first. OpenStreetMap tile requests are aborted so the suite
    /// stays hermetic and never depends on (or hammers) the public tile servers;
    /// the assertions ride on Leaflet's own DOM — the map container and the
    /// locally-served marker icons — and the marker-to-album navigation, none of
    /// which need tiles to paint.
    /// </summary>
    [TestFixture]
    public class MapTests : UITestBase
    {
        [SetUp]
        public async Task SignIn()
        {
            await SignInAsync();
        }

        [Test]
        public async Task Leaflet_initialises_the_map_container()
        {
            await BlockTilesAsync();
            await Page.GotoAsync(BaseUrl + "/");

            // Leaflet adds the leaflet-container class to the element it mounts on.
            await Expect(Page.Locator("#map.leaflet-container")).ToBeVisibleAsync();
        }

        [Test]
        public async Task Each_album_with_coordinates_gets_a_marker()
        {
            await BlockTilesAsync();
            await Page.GotoAsync(BaseUrl + "/");

            // One seeded album with lat/long -> exactly one Leaflet *image* marker.
            // Scope to img markers because a journey route's port pins are divIcon
            // markers (also .leaflet-marker-icon) drawn on the same map.
            await Expect(Page.Locator("img.leaflet-marker-icon")).ToHaveCountAsync(1);
        }

        [Test]
        public async Task Clicking_a_marker_navigates_to_its_album()
        {
            await BlockTilesAsync();
            await Page.GotoAsync(BaseUrl + "/");

            // Scope to the album image marker: a journey route's port pins are divIcon
            // markers (also .leaflet-marker-icon) and would otherwise match here.
            await Page.Locator("img.leaflet-marker-icon").First.ClickAsync();

            await Page.WaitForURLAsync(new Regex($"/album/{ServerFixture.SampleAlbumSlug}"));
        }

        [Test]
        public async Task Leaflet_assets_are_served_locally()
        {
            var js = await Page.APIRequest.GetAsync(BaseUrl + "/lib/leaflet/leaflet.js");
            var css = await Page.APIRequest.GetAsync(BaseUrl + "/lib/leaflet/leaflet.css");

            Assert.That(js.Ok, Is.True, "leaflet.js should be served from wwwroot/lib");
            Assert.That(css.Ok, Is.True, "leaflet.css should be served from wwwroot/lib");
        }

        [Test]
        public async Task Markercluster_assets_are_served_locally()
        {
            var js = await Page.APIRequest.GetAsync(BaseUrl + "/lib/leaflet.markercluster/leaflet.markercluster.js");
            var css = await Page.APIRequest.GetAsync(BaseUrl + "/lib/leaflet.markercluster/MarkerCluster.css");
            var themeCss = await Page.APIRequest.GetAsync(BaseUrl + "/lib/leaflet.markercluster/MarkerCluster.Default.css");

            Assert.Multiple(() =>
            {
                Assert.That(js.Ok, Is.True, "leaflet.markercluster.js should be served from wwwroot/lib (no CDN)");
                Assert.That(css.Ok, Is.True, "MarkerCluster.css should be served from wwwroot/lib");
                Assert.That(themeCss.Ok, Is.True, "MarkerCluster.Default.css should be served from wwwroot/lib");
            });
        }

        [Test]
        public async Task Hovering_a_marker_shows_a_tooltip_with_trip_details()
        {
            await BlockTilesAsync();
            await Page.GotoAsync(BaseUrl + "/");

            // A lone album renders as an ordinary (unclustered) image marker.
            await Page.Locator("img.leaflet-marker-icon").First.HoverAsync();

            // The hover tooltip carries the trip name so you can confirm the album
            // before clicking through to it.
            await Expect(Page.Locator(".leaflet-tooltip")).ToContainTextAsync(ServerFixture.SampleAlbumTitle);
        }

        [Test]
        public async Task Trips_at_the_same_location_cluster_into_one_counted_pin()
        {
            await BlockTilesAsync();

            // Serve a synthetic two-trips-at-the-exact-same-spot marker set so the
            // client-side clustering is exercised deterministically, independent of
            // what other tests have done to the seeded album.
            await StubMarkersAsync(
                new(55.95, -3.19, "trip-one", "Trip One"),
                new(55.95, -3.19, "trip-two", "Trip Two"));

            await Page.GotoAsync(BaseUrl + "/");

            // Two overlapping pins collapse into a single cluster badge showing the
            // count, instead of stacking invisibly on top of one another.
            var cluster = Page.Locator(".marker-cluster");
            await Expect(cluster).ToHaveCountAsync(1);
            await Expect(cluster).ToContainTextAsync("2");
        }

        [Test]
        public async Task Clicking_a_cluster_reveals_the_individual_trips()
        {
            await BlockTilesAsync();

            await StubMarkersAsync(
                new(55.95, -3.19, "trip-one", "Trip One"),
                new(55.95, -3.19, "trip-two", "Trip Two"));

            await Page.GotoAsync(BaseUrl + "/");

            await Page.Locator(".marker-cluster").ClickAsync();

            // Spiderfying the overlapping cluster fans the two trips out as separate,
            // individually selectable image markers — so they can be told apart.
            await Expect(Page.Locator("img.leaflet-marker-icon")).ToHaveCountAsync(2);
        }

        [Test]
        public async Task Home_page_uses_openstreetmap_not_google_maps()
        {
            var response = await Page.APIRequest.GetAsync(BaseUrl + "/");
            var body = await response.TextAsync();

            Assert.That(body, Does.Not.Contain("maps.googleapis.com"), "the Google Maps script must be gone");
            Assert.That(body, Does.Contain("/lib/leaflet/leaflet.js"), "the page should load Leaflet");
        }

        [Test]
        public async Task Startup_rebuilds_markers_and_prunes_drift()
        {
            // ServerFixture seeds markers.json with a stale marker for an album that
            // does not exist; the on-startup rebuild must have replaced the file with
            // the real album set, dropping the ghost.
            var response = await Page.APIRequest.GetAsync($"{BaseUrl}/albums/markers.json?nocache={System.Guid.NewGuid():N}");
            var json = await response.JsonAsync();
            var slugs = json!.Value.EnumerateArray().Select(m => m.GetProperty("Slug").GetString()).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(slugs, Does.Contain(ServerFixture.SampleAlbumSlug), "the real album's marker should be present");
                Assert.That(slugs, Does.Not.Contain("removed-album"), "the stale marker should be pruned on startup");
            });
        }

        [Test]
        public async Task Map_page_has_no_content_security_policy_violations()
        {
            // Collect any CSP violations the browser reports as the map renders, so the
            // now-enforced policy can't silently start blocking what the map needs.
            await Page.AddInitScriptAsync(
                "window.__cspViolations = [];"
                + "document.addEventListener('securitypolicyviolation',"
                + " function (e) { window.__cspViolations.push(e.violatedDirective + ' blocked ' + e.blockedURI); });");

            await BlockTilesAsync();
            await Page.GotoAsync(BaseUrl + "/");

            await Expect(Page.Locator("#map.leaflet-container")).ToBeVisibleAsync();
            await Expect(Page.Locator(".leaflet-marker-icon").First).ToBeVisibleAsync();

            var violations = await Page.EvaluateAsync<string[]>("() => window.__cspViolations");
            Assert.That(violations, Is.Empty, "the enforced CSP must not block anything the map needs");
        }

        private Task BlockTilesAsync()
        {
            return Page.RouteAsync("**/tile.openstreetmap.org/**", route => route.AbortAsync());
        }

        // Intercepts the map's marker fetch and returns a crafted set, so a test can
        // stand up any arrangement of pins (e.g. two trips sharing a location)
        // without mutating the shared seeded album. Must be called before GotoAsync.
        private Task StubMarkersAsync(params StubMarker[] markers)
        {
            var body = JsonSerializer.Serialize(markers.Select(m => new
            {
                m.Lat,
                m.Long,
                m.Slug,
                m.Name,
                Date = "Jan 2026",
                Photos = 0,
            }));

            return Page.RouteAsync("**/albums/markers.json**", route => route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = body,
            }));
        }

        private sealed record StubMarker(double Lat, double Long, string Slug, string Name);
    }
}