using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// End-to-end coverage for per-stop cruise photos: an admin uploads a photo to a
    /// day at sea through that stop's upload modal, the photo appears under that day
    /// with a generated thumbnail served from the cruise media path, and deleting it
    /// through the grid control removes it. The cruise is created and deleted within
    /// the test so the suite stays self-contained.
    /// </summary>
    [TestFixture]
    public class CruisePhotoTests : UITestBase
    {
        // An 8x8 PNG SkiaSharp decodes cleanly (matches the album upload tests).
        private static readonly byte[] SamplePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAABHNCSVQICAgIfAhkiAAAABZJREFUGJVjTJn69j8DHsCET3L4KAAA/T0C9UyjKGsAAAAASUVORK5CYII=");

        [Test]
        public async Task Uploading_a_photo_to_an_at_sea_stop_shows_it_and_delete_removes_it()
        {
            await SignInAsync();

            var title = "Photo Cruise " + System.Guid.NewGuid().ToString("N");
            string? slug = null;

            try
            {
                // Create a cruise with a single at-sea stop, which is assigned a
                // stable key server-side.
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddCruiseModalAsync();
                await Page.FillAsync("#cruiseName", title);
                await Page.FillAsync("#cruiseStart", "2025-07-15");
                await Page.FillAsync("#cruiseEnd", "2025-07-22");
                await Page.ClickAsync("#addCruiseDialog [data-add-stop]");
                await Page.FillAsync("input[name='stops[0].Date']", "2025-07-16");
                await Page.FillAsync("input[name='stops[0].Name']", "Cruising");
                await Page.CheckAsync("input[name='stops[0].AtSea']");
                await Page.ClickAsync("#newcruise");
                await Page.WaitForURLAsync(new Regex("/cruise/[^/]+/$"));
                slug = Regex.Match(Page.Url, "/cruise/([^/]+)/").Groups[1].Value;

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
                await Page.WaitForURLAsync(new Regex($"/cruise/{slug}/$"));

                // The photo now shows under that day with a served thumbnail from the
                // cruise media path.
                await Expect(Page.Locator(".itinerary-photos-row .thumb")).ToHaveCountAsync(1);
                var thumbSrc = await Page.GetAttributeAsync(".itinerary-photos-row .thumb img", "src");
                Assert.That(thumbSrc, Does.Contain("/cruises/"), "the thumbnail should be served from the cruise media path");
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
                    await DeleteCruiseAsync(slug);
                }
            }
        }
    }
}