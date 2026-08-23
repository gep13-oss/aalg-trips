using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Covers the journey kinds: creating a journey with a kind other than the default
    /// journey (a trek) makes the home page group it under its own "Treks" section and
    /// makes the detail page read with trek wording ("Edit trek"), while the seeded
    /// journey keeps its own "Cruises" section — so the home page groups journeys by
    /// kind. The trek is created and deleted within the test so the suite stays
    /// self-contained.
    /// </summary>
    [TestFixture]
    public class JourneyKindTests : UITestBase
    {
        [Test]
        public async Task Creating_a_trek_groups_it_under_treks_with_trek_wording()
        {
            await SignInAsync();

            var title = "Trek " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                // Create a journey of kind Trek through the Add-journey modal.
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddJourneyModalAsync();
                await Page.FillAsync("#journeyName", title);
                await Page.SelectOptionAsync("#journeyKind", "Trek");
                await Page.FillAsync("#journeyStart", "2025-05-01");
                await Page.FillAsync("#journeyEnd", "2025-05-08");
                await Page.ClickAsync("#newjourney");
                await Page.WaitForURLAsync(new Regex("/journey/[^/]+/$"));
                slug = Regex.Match(Page.Url, "/journey/([^/]+)/").Groups[1].Value;

                // The detail page reads with trek wording, not journey wording.
                await Page.ClickAsync("summary.actions-menu__trigger");
                await Page.WaitForSelectorAsync(".actions-menu[open]");
                await Expect(Page.Locator("[data-open-dialog='#editJourneyDialog']")).ToHaveTextAsync("Edit trek");

                // The home page groups it under its own "Treks" section, alongside the
                // seeded journey's "Cruises" section.
                await Page.GotoAsync(BaseUrl + "/");
                var treks = Page.Locator("section.journeys[data-journey-kind='Trek']");
                await Expect(treks).ToBeVisibleAsync();
                await Expect(treks.Locator(".section-head__title")).ToHaveTextAsync("Treks");
                await Expect(treks.Locator($".journey-card[href='/journey/{slug}/']")).ToHaveCountAsync(1);
                await Expect(Page.Locator("section.journeys[data-journey-kind='Cruise']")).ToBeVisibleAsync();
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