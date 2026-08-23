namespace AalgTrips.UITests
{
    /// <summary>
    /// Rendering coverage for the journey detail page, driven by the seeded
    /// <c>sample-journey</c>: the itinerary table lists every stop (including the day
    /// at sea), a port's linked trip shows both as an itinerary link and as a reused
    /// trip card, and the admin Actions menu is present for an admin but not a viewer.
    /// </summary>
    [TestFixture]
    public class JourneyDetailTests : UITestBase
    {
        private static string JourneyUrl => $"{BaseUrl}/journey/{ServerFixture.SampleJourneySlug}/";

        [Test]
        public async Task Journey_detail_page_renders_the_itinerary_and_linked_trip()
        {
            await SignInAsync();
            await Page.GotoAsync(JourneyUrl);

            // The seeded journey has three stops, so three itinerary stop rows (each
            // may be followed by a photos row, so scope the count to the stop rows).
            await Expect(Page.Locator(".itinerary-table tbody tr.itinerary-row")).ToHaveCountAsync(3);

            // All three stop names are shown, including the day at sea.
            var content = await Page.ContentAsync();
            Assert.Multiple(() =>
            {
                Assert.That(content, Does.Contain("Rome"));
                Assert.That(content, Does.Contain("Cruising"));
                Assert.That(content, Does.Contain("Santorini"));
            });

            // The day at sea is tagged as such.
            await Expect(Page.Locator(".itinerary-row--travel")).ToHaveCountAsync(1);

            // The port that links the sample album surfaces it both as an itinerary
            // link and as a reused trip card lower down the page.
            await Expect(Page.Locator($".itinerary-trips a[href='/album/{ServerFixture.SampleAlbumSlug}/']")).ToBeVisibleAsync();
            await Expect(Page.Locator($".trip-card[href='/album/{ServerFixture.SampleAlbumSlug}/']")).ToBeVisibleAsync();
        }

        [Test]
        public async Task Journey_actions_menu_is_available_to_an_admin()
        {
            await SignInAsync();
            await Page.GotoAsync(JourneyUrl);

            // The trigger is shown, but its items stay hidden until the menu opens.
            await Expect(Page.Locator("summary.actions-menu__trigger")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-open-dialog='#editJourneyDialog']")).ToBeHiddenAsync();

            await Page.ClickAsync("summary.actions-menu__trigger");
            await Page.WaitForSelectorAsync(".actions-menu[open]");

            await Expect(Page.Locator("[data-open-dialog='#editJourneyDialog']")).ToBeVisibleAsync();
            await Expect(Page.Locator("[data-open-dialog='#renameJourneyDialog']")).ToBeVisibleAsync();
            await Expect(Page.Locator("#deletejourney")).ToBeVisibleAsync();
        }

        [Test]
        public async Task A_viewer_does_not_see_the_journey_actions()
        {
            await SignInAsViewerAsync();
            await Page.GotoAsync(JourneyUrl);

            // The whole admin block is gated behind the admin role, so a viewer sees
            // no Actions trigger at all.
            await Expect(Page.Locator("summary.actions-menu__trigger")).ToHaveCountAsync(0);
        }
    }
}