using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Covers the cruise map + home integration: map.js draws each cruise as a route
    /// line through its ports, a distinct port pin per port, and a dotted connector
    /// from a port to each trip done from it; and the home page lists cruise cards
    /// that link through to the detail page. The map assertions ride on stubbed
    /// markers/cruises so they are independent of the shared seeded state.
    /// </summary>
    [TestFixture]
    public class CruiseMapTests : UITestBase
    {
        [SetUp]
        public async Task SignIn()
        {
            await SignInAsync();
        }

        [Test]
        public async Task A_cruise_route_is_drawn_with_port_pins_and_a_trip_connector()
        {
            await BlockTilesAsync();

            // One trip (its pin resolves the connector target) and a two-port cruise
            // whose first port links that trip; the second port links nothing.
            await StubMarkersAsync(41.89, 12.49, "colosseum", "Colosseum");
            await StubCruisesAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-cruise",
                    Name = "Med Cruise",
                    Color = "#e11d48",
                    Ports = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "15 Jul 2025", Arrive = (string?)null, Depart = "18:00", Trips = new[] { "colosseum" } },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Date = "18 Jul 2025", Arrive = (string?)"07:00", Depart = "17:00", Trips = System.Array.Empty<string>() },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            // Two ports -> two port pins and one route line through them.
            await Expect(Page.Locator(".port-pin")).ToHaveCountAsync(2);
            await Expect(Page.Locator("path.cruise-route")).ToHaveCountAsync(1);

            // Only the first port links a trip, so exactly one dotted connector.
            await Expect(Page.Locator("path.cruise-connector")).ToHaveCountAsync(1);

            // The route is drawn in the cruise's chosen colour.
            await Expect(Page.Locator("path.cruise-route")).ToHaveAttributeAsync("stroke", "#e11d48");

            // The port pins are numbered in visit order.
            await Expect(Page.Locator(".port-pin__num").Nth(0)).ToHaveTextAsync("1");
            await Expect(Page.Locator(".port-pin__num").Nth(1)).ToHaveTextAsync("2");
        }

        [Test]
        public async Task A_round_trip_port_draws_one_pin_badged_with_both_visit_numbers()
        {
            await BlockTilesAsync();

            // A round trip: Rome is both the first stop (embark) and the third
            // (return) at the same coordinates, with Naples in between. The shared
            // port must collapse into a single pin so the return does not hide the
            // start.
            await StubMarkersAsync(41.89, 12.49, "colosseum", "Colosseum");
            await StubCruisesAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-cruise",
                    Name = "Med Cruise",
                    Color = "#e11d48",
                    Ports = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "15 Jul 2025", Arrive = (string?)null, Depart = (string?)"18:00", Trips = new[] { "colosseum" } },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Date = "18 Jul 2025", Arrive = (string?)"07:00", Depart = (string?)"17:00", Trips = System.Array.Empty<string>() },
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "21 Jul 2025", Arrive = (string?)"06:00", Depart = (string?)null, Trips = System.Array.Empty<string>() },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            // Three stops but only two distinct locations -> two pins.
            await Expect(Page.Locator(".port-pin")).ToHaveCountAsync(2);

            // The shared start/end port is badged with both of its visit numbers.
            var combined = Page.Locator(".port-pin__num", new PageLocatorOptions { HasTextString = "1 · 3" });
            var single = Page.Locator(".port-pin__num", new PageLocatorOptions { HasTextString = "2" });
            await Expect(combined).ToHaveCountAsync(1);
            await Expect(single).ToHaveCountAsync(1);

            // The combined badge must grow to fit both numbers rather than clip them
            // inside the single-digit box.
            var combinedBox = await combined.BoundingBoxAsync();
            var singleBox = await single.BoundingBoxAsync();
            Assert.That(combinedBox!.Width, Is.GreaterThan(singleBox!.Width), "the '1 · 3' badge should be wider than a single-number pin");
        }

        [Test]
        public async Task An_uploaded_route_is_drawn_along_its_geometry_not_straight_port_lines()
        {
            await BlockTilesAsync();

            // A two-port cruise that also carries an uploaded route: a four-point line
            // between the ports (a stand-in for an offline-computed sea route). The
            // drawn polyline should follow all four points, not join the two ports
            // with a single straight segment.
            await StubMarkersAsync(41.89, 12.49, "colosseum", "Colosseum");
            await StubCruisesAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-cruise",
                    Name = "Med Cruise",
                    Color = "#e11d48",
                    Ports = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Trips = System.Array.Empty<string>() },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Trips = System.Array.Empty<string>() },
                    },
                    Geometry = new[]
                    {
                        new[] { 41.90, 12.50 },
                        new[] { 41.50, 13.00 },
                        new[] { 41.00, 13.80 },
                        new[] { 40.85, 14.27 },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            await Expect(Page.Locator("path.cruise-route")).ToHaveCountAsync(1);

            // Leaflet renders the polyline as "M…L…L…L…"; a route through 4 points has
            // exactly three line segments, where the 2-port fallback would have one.
            await Expect(Page.Locator("path.cruise-route"))
                .ToHaveAttributeAsync("d", new Regex(@"^M[^L]*(?:L[^L]*){3}$"));
        }

        [Test]
        public async Task Home_page_lists_cruise_cards_that_link_through()
        {
            await Page.GotoAsync(BaseUrl + "/");

            var card = Page.Locator($".cruise-card[href='/cruise/{ServerFixture.SampleCruiseSlug}/']");
            await Expect(card).ToHaveCountAsync(1);

            await card.ClickAsync();
            await Page.WaitForURLAsync(new Regex($"/cruise/{ServerFixture.SampleCruiseSlug}/$"));

            // The detail page for the seeded cruise renders its itinerary.
            await Expect(Page.Locator(".itinerary-table")).ToBeVisibleAsync();
        }

        private Task BlockTilesAsync()
        {
            return Page.RouteAsync("**/tile.openstreetmap.org/**", route => route.AbortAsync());
        }

        private Task StubMarkersAsync(double lat, double lng, string slug, string name)
        {
            var body = JsonSerializer.Serialize(new[]
            {
                new { Lat = lat, Long = lng, Slug = slug, Name = name, Date = "Jul 2025", Photos = 0 },
            });

            return Page.RouteAsync("**/albums/markers.json**", route => route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = body,
            }));
        }

        private Task StubCruisesAsync(string body)
        {
            return Page.RouteAsync("**/albums/cruises.json**", route => route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = body,
            }));
        }
    }
}