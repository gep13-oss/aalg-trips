using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Phase 5 polish: a trip that a cruise links shows a "Part of the … cruise"
    /// backlink on its album page, and the home-page "Cruises" toggle shows or hides
    /// the cruise cards and their map routes independently of the trip filters. Both
    /// ride on the seeded sample cruise, whose Rome stop links the sample trip.
    /// </summary>
    [TestFixture]
    public class CruiseIntegrationTests : UITestBase
    {
        [Test]
        public async Task A_linked_trip_page_shows_a_backlink_to_its_cruise()
        {
            await SignInAsync();
            await Page.GotoAsync($"{BaseUrl}/album/{ServerFixture.SampleAlbumSlug}/");

            var link = Page.Locator($".album-head__cruise-link[href='/cruise/{ServerFixture.SampleCruiseSlug}/']");
            await Expect(link).ToBeVisibleAsync();
            await Expect(link).ToContainTextAsync(ServerFixture.SampleCruiseTitle);

            await link.ClickAsync();
            await Page.WaitForURLAsync(new Regex($"/cruise/{ServerFixture.SampleCruiseSlug}/$"));
        }

        [Test]
        public async Task Cruises_toggle_hides_the_cruise_section_and_its_routes()
        {
            await SignInAsync();
            await Page.RouteAsync("**/tile.openstreetmap.org/**", route => route.AbortAsync());
            await Page.GotoAsync(BaseUrl + "/");

            var section = Page.Locator("section.cruises");

            // Cruises and their route pins are shown by default.
            await Expect(section).ToBeVisibleAsync();
            await Expect(Page.Locator(".port-pin").First).ToBeVisibleAsync();

            // The chip's checkbox is visually hidden (styled via :has()), so toggle it
            // by clicking the chip label, as the trip-filter tests do. It starts
            // checked, so the first click turns cruises off.
            await Page.ClickAsync("label.chip:has(.filter-cruises)");
            await Expect(section).ToBeHiddenAsync();
            await Expect(Page.Locator(".port-pin")).ToHaveCountAsync(0);

            // Clicking again turns them back on.
            await Page.ClickAsync("label.chip:has(.filter-cruises)");
            await Expect(section).ToBeVisibleAsync();
            await Expect(Page.Locator(".port-pin").First).ToBeVisibleAsync();
        }
    }
}