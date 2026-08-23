using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Covers the journey map + home integration: map.js draws each journey as a route
    /// line through its ports, a distinct port pin per port, and a dotted connector
    /// from a port to each trip done from it; and the home page lists journey cards
    /// that link through to the detail page. The map assertions ride on stubbed
    /// markers/journeys so they are independent of the shared seeded state.
    /// </summary>
    [TestFixture]
    public class JourneyMapTests : UITestBase
    {
        [SetUp]
        public async Task SignIn()
        {
            await SignInAsync();
        }

        [Test]
        public async Task A_journey_route_is_drawn_with_port_pins_and_a_trip_connector()
        {
            await BlockTilesAsync();

            // One trip (its pin resolves the connector target) and a two-port journey
            // whose first port links that trip; the second port links nothing.
            await StubMarkersAsync(41.89, 12.49, "colosseum", "Colosseum");
            await StubJourneysAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-journey",
                    Name = "Med Journey",
                    Color = "#e11d48",
                    Waypoints = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "15 Jul 2025", Arrive = (string?)null, Depart = "18:00", Trips = new[] { "colosseum" } },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Date = "18 Jul 2025", Arrive = (string?)"07:00", Depart = "17:00", Trips = System.Array.Empty<string>() },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            // Two ports -> two port pins and one route line through them.
            await Expect(Page.Locator(".route-pin")).ToHaveCountAsync(2);
            await Expect(Page.Locator("path.journey-route")).ToHaveCountAsync(1);

            // Only the first port links a trip, so exactly one dotted connector.
            await Expect(Page.Locator("path.journey-connector")).ToHaveCountAsync(1);

            // The route is drawn in the journey's chosen colour.
            await Expect(Page.Locator("path.journey-route")).ToHaveAttributeAsync("stroke", "#e11d48");

            // The port pins are numbered in visit order.
            await Expect(Page.Locator(".route-pin__num").Nth(0)).ToHaveTextAsync("1");
            await Expect(Page.Locator(".route-pin__num").Nth(1)).ToHaveTextAsync("2");
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
            await StubJourneysAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-journey",
                    Name = "Med Journey",
                    Color = "#e11d48",
                    Waypoints = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "15 Jul 2025", Arrive = (string?)null, Depart = (string?)"18:00", Trips = new[] { "colosseum" } },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Date = "18 Jul 2025", Arrive = (string?)"07:00", Depart = (string?)"17:00", Trips = System.Array.Empty<string>() },
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Date = "21 Jul 2025", Arrive = (string?)"06:00", Depart = (string?)null, Trips = System.Array.Empty<string>() },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            // Three stops but only two distinct locations -> two pins.
            await Expect(Page.Locator(".route-pin")).ToHaveCountAsync(2);

            // The shared start/end port is badged with both of its visit numbers.
            var combined = Page.Locator(".route-pin__num", new PageLocatorOptions { HasTextString = "1 · 3" });
            var single = Page.Locator(".route-pin__num", new PageLocatorOptions { HasTextString = "2" });
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

            // A two-port journey that also carries an uploaded route: a four-point line
            // between the ports (a stand-in for an offline-computed sea route). The
            // drawn polyline should follow all four points, not join the two ports
            // with a single straight segment.
            await StubMarkersAsync(41.89, 12.49, "colosseum", "Colosseum");
            await StubJourneysAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "med-journey",
                    Name = "Med Journey",
                    Color = "#e11d48",
                    Waypoints = new[]
                    {
                        new { Lat = 41.90, Long = 12.50, Name = "Rome", Trips = System.Array.Empty<string>() },
                        new { Lat = 40.85, Long = 14.27, Name = "Naples", Trips = System.Array.Empty<string>() },
                    },
                    Geometry = new[]
                    {
                        new
                        {
                            Points = new[]
                            {
                                new[] { 41.90, 12.50 },
                                new[] { 41.50, 13.00 },
                                new[] { 41.00, 13.80 },
                                new[] { 40.85, 14.27 },
                            },
                            Travel = false,
                        },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            await Expect(Page.Locator("path.journey-route")).ToHaveCountAsync(1);

            // Leaflet renders the polyline as "M…L…L…L…"; a route through 4 points has
            // exactly three line segments, where the 2-port fallback would have one.
            await Expect(Page.Locator("path.journey-route"))
                .ToHaveAttributeAsync("d", new Regex(@"^M[^L]*(?:L[^L]*){3}$"));
        }

        [Test]
        public async Task A_travel_segment_is_drawn_dashed_and_a_covered_segment_solid()
        {
            await BlockTilesAsync();

            // A two-segment route: a solid covered track then a dashed travel hop (a
            // flight from Beijing to Xi'an), as a trek's uploaded route would carry.
            await StubMarkersAsync(39.90, 116.40, "forbidden-city", "Forbidden City");
            await StubJourneysAsync(JsonSerializer.Serialize(new[]
            {
                new
                {
                    Slug = "great-wall",
                    Name = "Great Wall Trek",
                    Kind = "Trek",
                    Color = "#e11d48",
                    Waypoints = new[]
                    {
                        new { Lat = 40.00, Long = 116.00, Name = "Beijing", Trips = System.Array.Empty<string>() },
                        new { Lat = 34.26, Long = 108.94, Name = "Xi'an", Trips = System.Array.Empty<string>() },
                    },
                    Geometry = new object[]
                    {
                        new { Points = new[] { new[] { 40.00, 116.00 }, new[] { 39.90, 116.40 } }, Travel = false },
                        new { Points = new[] { new[] { 39.90, 116.40 }, new[] { 34.26, 108.94 } }, Travel = true },
                    },
                },
            }));

            await Page.GotoAsync(BaseUrl + "/");

            // Two segments -> two route polylines; exactly one of them is dashed.
            await Expect(Page.Locator("path.journey-route")).ToHaveCountAsync(2);
            await Expect(Page.Locator("path.journey-route[stroke-dasharray]")).ToHaveCountAsync(1);
        }

        [Test]
        public async Task Home_page_lists_journey_cards_that_link_through()
        {
            await Page.GotoAsync(BaseUrl + "/");

            var card = Page.Locator($".journey-card[href='/journey/{ServerFixture.SampleJourneySlug}/']");
            await Expect(card).ToHaveCountAsync(1);

            await card.ClickAsync();
            await Page.WaitForURLAsync(new Regex($"/journey/{ServerFixture.SampleJourneySlug}/$"));

            // The detail page for the seeded journey renders its itinerary.
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

        private Task StubJourneysAsync(string body)
        {
            return Page.RouteAsync("**/albums/journeys.json**", route => route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = body,
            }));
        }
    }
}