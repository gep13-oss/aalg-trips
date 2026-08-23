using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Phase 5 polish: a trip that a journey links shows a "Part of the … journey"
    /// backlink on its album page, and the home-page "Journeys" toggle shows or hides
    /// the journey cards and their map routes independently of the trip filters. Both
    /// ride on the seeded sample journey, whose Rome stop links the sample trip.
    /// </summary>
    [TestFixture]
    public class JourneyIntegrationTests : UITestBase
    {
        [Test]
        public async Task A_linked_trip_page_shows_a_backlink_to_its_journey()
        {
            await SignInAsync();
            await Page.GotoAsync($"{BaseUrl}/album/{ServerFixture.SampleAlbumSlug}/");

            var link = Page.Locator($".album-head__journey-link[href='/journey/{ServerFixture.SampleJourneySlug}/']");
            await Expect(link).ToBeVisibleAsync();
            await Expect(link).ToContainTextAsync(ServerFixture.SampleJourneyTitle);

            await link.ClickAsync();
            await Page.WaitForURLAsync(new Regex($"/journey/{ServerFixture.SampleJourneySlug}/$"));
        }

        [Test]
        public async Task Journeys_toggle_hides_the_journey_section_and_its_routes()
        {
            await SignInAsync();
            await Page.RouteAsync("**/tile.openstreetmap.org/**", route => route.AbortAsync());
            await Page.GotoAsync(BaseUrl + "/");

            var section = Page.Locator("section.journeys[data-journey-kind='Cruise']");

            // Journeys and their route pins are shown by default.
            await Expect(section).ToBeVisibleAsync();
            await Expect(Page.Locator(".route-pin").First).ToBeVisibleAsync();

            // The chip's checkbox is visually hidden (styled via :has()), so toggle it
            // by clicking the chip label, as the trip-filter tests do. It starts
            // checked, so the first click turns the Cruises kind off.
            await Page.ClickAsync("label.chip:has(.filter-journeys[data-kind='Cruise'])");
            await Expect(section).ToBeHiddenAsync();
            await Expect(Page.Locator(".route-pin")).ToHaveCountAsync(0);

            // Clicking again turns them back on.
            await Page.ClickAsync("label.chip:has(.filter-journeys[data-kind='Cruise'])");
            await Expect(section).ToBeVisibleAsync();
            await Expect(Page.Locator(".route-pin").First).ToBeVisibleAsync();
        }
    }
}