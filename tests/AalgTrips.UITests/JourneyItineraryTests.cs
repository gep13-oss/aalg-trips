using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Coverage for the dynamic itinerary editor: stop rows can be added and removed
    /// in the create modal, and a stop that links a trip album carries that link
    /// through to the saved journey (both the itinerary table and the trip cards).
    /// </summary>
    [TestFixture]
    public class JourneyItineraryTests : UITestBase
    {
        [Test]
        public async Task Itinerary_editor_adds_and_removes_stop_rows()
        {
            await SignInAsync();
            await Page.GotoAsync(BaseUrl + "/");
            await OpenAddJourneyModalAsync();

            var rows = Page.Locator("#addJourneyDialog [data-stop-row]");

            // The template row lives in a <template> and is not part of the live DOM,
            // so the editor starts with no rows.
            await Expect(rows).ToHaveCountAsync(0);

            await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
            await Expect(rows).ToHaveCountAsync(1);

            await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
            await Expect(rows).ToHaveCountAsync(2);

            await Page.ClickAsync("#addJourneyDialog [data-stop-row]:first-child [data-remove-stop]");
            await Expect(rows).ToHaveCountAsync(1);
        }

        [Test]
        public async Task Stops_are_ordered_by_date_regardless_of_entry_order()
        {
            await SignInAsync();

            var title = "Ordering Journey " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddJourneyModalAsync();
                await Page.FillAsync("#journeyName", title);
                await Page.FillAsync("#journeyStart", "2025-07-15");
                await Page.FillAsync("#journeyEnd", "2025-07-22");

                // Add the later stop first, then an earlier one — out of date order.
                await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
                await Page.FillAsync("input[name='stops[0].Date']", "2025-07-20");
                await Page.FillAsync("input[name='stops[0].Name']", "Naples");

                await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
                await Page.FillAsync("input[name='stops[1].Date']", "2025-07-16");
                await Page.FillAsync("input[name='stops[1].Name']", "Rome");

                await Page.ClickAsync("#newjourney");
                await Page.WaitForURLAsync(new Regex("/journey/[^/]+/$"));
                slug = Regex.Match(Page.Url, "/journey/([^/]+)/").Groups[1].Value;

                // The saved itinerary is chronological: Rome (16th) before Naples (20th).
                var ports = Page.Locator(".itinerary-row .itinerary-stop");
                await Expect(ports.Nth(0)).ToContainTextAsync("Rome");
                await Expect(ports.Nth(1)).ToContainTextAsync("Naples");
            }
            finally
            {
                if (slug != null)
                {
                    await DeleteJourneyAsync(slug);
                }
            }
        }

        [Test]
        public async Task A_journey_stop_can_link_a_trip_album()
        {
            await SignInAsync();

            var title = "Itinerary Journey " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddJourneyModalAsync();
                await Page.FillAsync("#journeyName", title);
                await Page.FillAsync("#journeyStart", "2025-07-15");
                await Page.FillAsync("#journeyEnd", "2025-07-22");

                await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
                await Page.FillAsync("input[name='stops[0].Date']", "2025-07-15");
                await Page.FillAsync("input[name='stops[0].Name']", "Rome");
                await Page.SelectOptionAsync("select[name='stops[0].Trips']", new[] { ServerFixture.SampleAlbumSlug });

                await Page.ClickAsync("#newjourney");
                await Page.WaitForURLAsync(new Regex("/journey/[^/]+/$"));
                slug = Regex.Match(Page.Url, "/journey/([^/]+)/").Groups[1].Value;

                // The linked trip carries through to the saved journey, both as an
                // itinerary link and as a reused trip card.
                await Expect(Page.Locator($".itinerary-trips a[href='/album/{ServerFixture.SampleAlbumSlug}/']")).ToBeVisibleAsync();
                await Expect(Page.Locator($".trip-card[href='/album/{ServerFixture.SampleAlbumSlug}/']")).ToBeVisibleAsync();
            }
            finally
            {
                if (slug != null)
                {
                    await DeleteJourneyAsync(slug);
                }
            }
        }
    }
}