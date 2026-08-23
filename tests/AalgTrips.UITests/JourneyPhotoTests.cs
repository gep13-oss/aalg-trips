using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// End-to-end coverage for per-stop journey photos: an admin uploads a photo to a
    /// day at sea through that stop's upload modal, the photo appears under that day
    /// with a generated thumbnail served from the journey media path, and deleting it
    /// through the grid control removes it. The journey is created and deleted within
    /// the test so the suite stays self-contained.
    /// </summary>
    [TestFixture]
    public class JourneyPhotoTests : UITestBase
    {
        // An 8x8 PNG SkiaSharp decodes cleanly (matches the album upload tests).
        private static readonly byte[] SamplePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAABHNCSVQICAgIfAhkiAAAABZJREFUGJVjTJn69j8DHsCET3L4KAAA/T0C9UyjKGsAAAAASUVORK5CYII=");

        [Test]
        public async Task Uploading_a_photo_to_an_at_sea_stop_shows_it_and_delete_removes_it()
        {
            await SignInAsync();

            var title = "Photo Journey " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                // Create a journey with a single at-sea stop, which is assigned a
                // stable key server-side.
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddJourneyModalAsync();
                await Page.FillAsync("#journeyName", title);
                await Page.FillAsync("#journeyStart", "2025-07-15");
                await Page.FillAsync("#journeyEnd", "2025-07-22");
                await Page.ClickAsync("#addJourneyDialog [data-add-stop]");
                await Page.FillAsync("input[name='stops[0].Date']", "2025-07-16");
                await Page.FillAsync("input[name='stops[0].Name']", "Cruising");
                await Page.CheckAsync("input[name='stops[0].AtSea']");
                await Page.ClickAsync("#newjourney");
                await Page.WaitForURLAsync(new Regex("/journey/[^/]+/$"));
                slug = Regex.Match(Page.Url, "/journey/([^/]+)/").Groups[1].Value;

                // Upload a photo to that stop through its "Add photos" modal.
                await Page.ClickAsync("[data-upload-stop]");
                await Page.WaitForSelectorAsync("#uploadStopDialog[open]");
                await Page.SetInputFilesAsync("#stopFiles", new FilePayload
                {
                    Name = "at-sea.png",
                    MimeType = "image/png",
                    Buffer = SamplePng,
                });
                await Page.ClickAsync("#btnStopUpload");
                await Page.WaitForURLAsync(new Regex($"/journey/{slug}/$"));

                // The photo now shows under that day with a served thumbnail from the
                // journey media path.
                await Expect(Page.Locator(".itinerary-photos-row .thumb")).ToHaveCountAsync(1);
                var thumbSrc = await Page.GetAttributeAsync(".itinerary-photos-row .thumb img", "src");
                Assert.That(thumbSrc, Does.Contain("/journeys/"), "the thumbnail should be served from the journey media path");
                var thumb = await Page.APIRequest.GetAsync(BaseUrl + thumbSrc);
                Assert.That(thumb.Ok, Is.True, "the generated thumbnail should be served");

                // Delete it through the grid control (accepting the confirm) and it is
                // gone.
                Page.Dialog += AcceptDialog;
                await Page.Locator(".itinerary-photos-row .thumb__delete-btn").ClickAsync();
                await Expect(Page.Locator(".itinerary-photos-row .thumb")).ToHaveCountAsync(0);
                Page.Dialog -= AcceptDialog;
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