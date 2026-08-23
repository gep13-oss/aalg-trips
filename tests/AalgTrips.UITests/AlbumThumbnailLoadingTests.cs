using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Verifies album thumbnails are lazy-loaded. A trip album can hold dozens of
    /// photos, each served through the authenticated media endpoint; without
    /// <c>loading="lazy"</c> the browser fetches every thumbnail — including ones
    /// far below the fold — the moment the page opens, which is what made large
    /// albums slow to render. The thumbnail img must therefore carry
    /// <c>loading="lazy"</c> (and <c>decoding="async"</c>), while the layout stays
    /// stable via the width/height the tag helper already emits.
    /// </summary>
    [TestFixture]
    public class AlbumThumbnailLoadingTests : UITestBase
    {
        private static readonly byte[] SamplePng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAABHNCSVQICAgIfAhkiAAAABZJREFUGJVjTJn69j8DHsCET3L4KAAA/T0C9UyjKGsAAAAASUVORK5CYII=");

        [Test]
        public async Task Album_thumbnails_are_lazy_loaded()
        {
            await SignInAsync();

            await Page.GotoAsync($"{BaseUrl}/album/{ServerFixture.SampleAlbumSlug}/");
            await OpenAlbumActionAsync("uploadDialog");
            await Page.SetInputFilesAsync("#files", new FilePayload
            {
                Name = "lazy.png",
                MimeType = "image/png",
                Buffer = SamplePng,
            });
            await Page.ClickAsync("#btnfiles");
            await Page.WaitForURLAsync(new Regex($"/album/{ServerFixture.SampleAlbumSlug}/$"));

            var thumb = Page.Locator(".thumb img").First;
            await Expect(thumb).ToHaveAttributeAsync("loading", "lazy");
            await Expect(thumb).ToHaveAttributeAsync("decoding", "async");

            // The dimensions that keep the grid from reflowing as thumbnails arrive.
            await Expect(thumb).ToHaveAttributeAsync("width", "190");
            await Expect(thumb).ToHaveAttributeAsync("height", new Regex(@"\d+"));

            var token = await AntiforgeryTokenAsync($"/album/{ServerFixture.SampleAlbumSlug}/");
            await Page.APIRequest.PostAsync(
                $"{BaseUrl}/photo/{ServerFixture.SampleAlbumSlug}/lazy/delete",
                FormPost(token));
        }
    }
}