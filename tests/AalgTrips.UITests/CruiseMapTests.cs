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