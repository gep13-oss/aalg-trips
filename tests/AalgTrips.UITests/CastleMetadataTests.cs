using System.Text.Json;
using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Coverage for the "castle visited" trip flag: it round-trips through the
    /// Add/Edit modal, is carried onto the album's map marker in
    /// <c>markers.json</c>, and drives a distinct-colour pin on the home-page map
    /// while non-castle trips keep the default Leaflet marker. Each test creates a
    /// throwaway album and deletes it again so the shared sample album is untouched.
    /// </summary>
    [TestFixture]
    public class CastleMetadataTests : UITestBase
    {
        [Test]
        public async Task Creating_a_castle_trip_flags_its_marker()
        {
            await SignInAsync();

            var slug = await CreateCastleTripAsync(castle: true);

            try
            {
                var markers = await ReadMarkersAsync();

                var castleMarker = markers.SingleOrDefault(m => m.Slug == slug);
                Assert.That(castleMarker, Is.Not.Null, "the new castle trip should have a marker");
                Assert.That(castleMarker!.Castle, Is.True, "the castle trip's marker should carry Castle = true");

                // The seeded sample trip is not a castle, so its marker must stay false —
                // proving the flag is per-album, not a global default.
                var sampleMarker = markers.SingleOrDefault(m => m.Slug == ServerFixture.SampleAlbumSlug);
                Assert.That(sampleMarker, Is.Not.Null);
                Assert.That(sampleMarker!.Castle, Is.False, "a non-castle trip's marker should be Castle = false");
            }
            finally
            {
                await DeleteAlbumAsync(slug);
            }
        }

        [Test]
        public async Task Castle_flag_round_trips_through_the_edit_modal()
        {
            await SignInAsync();

            // Create it as a castle, then confirm the edit modal shows it ticked.
            var slug = await CreateCastleTripAsync(castle: true);

            try
            {
                await Page.GotoAsync($"{BaseUrl}/album/{slug}/");
                await OpenAlbumActionAsync("editDialog");
                await Expect(Page.Locator("#castleVisited")).ToBeCheckedAsync();

                // Untick and save; the marker must follow.
                await Page.UncheckAsync("#castleVisited");
                await Page.ClickAsync("#btnEdit");
                await Page.WaitForURLAsync(new Regex($"/album/{slug}/$"));

                var markers = await ReadMarkersAsync();
                Assert.That(markers.Single(m => m.Slug == slug).Castle, Is.False, "clearing the checkbox should clear the marker flag");

                // Re-open the edit modal: the checkbox now reflects the cleared state.
                await OpenAlbumActionAsync("editDialog");
                await Expect(Page.Locator("#castleVisited")).Not.ToBeCheckedAsync();
            }
            finally
            {
                await DeleteAlbumAsync(slug);
            }
        }

        [Test]
        public async Task Castle_trip_gets_a_distinct_pin_and_others_keep_the_default()
        {
            await SignInAsync();

            var slug = await CreateCastleTripAsync(castle: true);

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                // The castle trip renders as the custom divIcon pin, stamped with the
                // castle glyph (inline SVG) rather than a plain dot...
                await Expect(Page.Locator(".castle-pin")).ToBeVisibleAsync();
                await Expect(Page.Locator(".castle-pin .castle-pin__glyph")).ToBeVisibleAsync();

                // ...while at least one default Leaflet image marker (the non-castle
                // sample trip) is also on the map.
                await Expect(Page.Locator("img.leaflet-marker-icon").First).ToBeVisibleAsync();
            }
            finally
            {
                await DeleteAlbumAsync(slug);
            }
        }

        /// <summary>
        /// Creates a throwaway trip via the home-page "Add trip" modal, far from the
        /// sample album so its map pin never clusters with it, and returns its slug.
        /// </summary>
        /// <param name="castle">Whether to tick the "castle visited" checkbox.</param>
        /// <returns>The created album's slug, taken from the redirect URL.</returns>
        private async Task<string> CreateCastleTripAsync(bool castle)
        {
            await Page.GotoAsync(BaseUrl + "/");
            await OpenAddTripModalAsync();

            await Page.FillAsync("#name", "Castle " + System.Guid.NewGuid().ToString("N"));
            await Page.FillAsync("#visited", "2026-04-04");

            // Madrid — well away from the seeded Edinburgh sample so the two pins
            // stay separate (unclustered) on the fitted map.
            await Page.FillAsync("#latitude", "40.4168");
            await Page.FillAsync("#longitude", "-3.7038");

            if (castle)
            {
                await Page.CheckAsync("#castleVisited");
            }

            await Page.ClickAsync("#newalbum");
            await Page.WaitForURLAsync(new Regex("/album/[^/]+/$"));

            var match = Regex.Match(Page.Url, "/album/([^/]+)/$");
            return match.Groups[1].Value;
        }

        private async Task DeleteAlbumAsync(string slug)
        {
            var token = await AntiforgeryTokenAsync();
            await Page.APIRequest.PostAsync($"{BaseUrl}/album/{slug}/delete", FormPost(token));
        }

        private async Task<List<MarkerDto>> ReadMarkersAsync()
        {
            var response = await Page.APIRequest.GetAsync($"{BaseUrl}/albums/markers.json?nocache={System.Guid.NewGuid():N}");
            return JsonSerializer.Deserialize<List<MarkerDto>>(
                await response.TextAsync(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }

        private sealed class MarkerDto
        {
            public string Slug { get; set; } = string.Empty;

            public bool Castle { get; set; }
        }
    }
}