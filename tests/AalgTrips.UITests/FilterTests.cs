using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Coverage for the home-page trip filters. Selecting the "Castles" toggle or a
    /// person's chip hides the non-matching trip cards (and their map pins) and
    /// updates the trip count; clearing restores every trip. Each test creates
    /// throwaway albums, well away from the seeded sample, and deletes them again.
    /// </summary>
    [TestFixture]
    public class FilterTests : UITestBase
    {
        [Test]
        public async Task Castle_filter_shows_only_castle_trips()
        {
            await SignInAsync();

            var castleSlug = await CreateTripAsync(castle: true, people: System.Array.Empty<string>(), lat: "10.0", lng: "10.0");
            var plainSlug = await CreateTripAsync(castle: false, people: System.Array.Empty<string>(), lat: "-33.8", lng: "151.2");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");
                await Expect(Page.Locator("#filters")).ToBeVisibleAsync();

                await Page.ClickAsync("label.chip:has(.filter-castle)");

                await Expect(CardFor(castleSlug)).ToBeVisibleAsync();
                await Expect(CardFor(plainSlug)).ToBeHiddenAsync();

                // The map follows: only the castle pin remains, and no default
                // (non-castle) image markers are left on the map.
                await Expect(Page.Locator(".castle-pin")).ToHaveCountAsync(1);
                await Expect(Page.Locator("img.leaflet-marker-icon")).ToHaveCountAsync(0);
            }
            finally
            {
                await DeleteAlbumAsync(castleSlug);
                await DeleteAlbumAsync(plainSlug);
            }
        }

        [Test]
        public async Task Person_filter_shows_only_that_persons_trips()
        {
            await SignInAsync();

            var ameliaSlug = await CreateTripAsync(castle: false, people: new[] { "Amelia" }, lat: "20.0", lng: "20.0");
            var garySlug = await CreateTripAsync(castle: false, people: new[] { "Gary" }, lat: "40.0", lng: "40.0");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                await Page.ClickAsync("label.chip:has(input[value='Amelia'])");

                await Expect(CardFor(ameliaSlug)).ToBeVisibleAsync();
                await Expect(CardFor(garySlug)).ToBeHiddenAsync();
                await Expect(Page.Locator(".trips .section-head__count")).ToHaveTextAsync("1 trip");
            }
            finally
            {
                await DeleteAlbumAsync(ameliaSlug);
                await DeleteAlbumAsync(garySlug);
            }
        }

        [Test]
        public async Task Selecting_multiple_people_requires_all_of_them()
        {
            await SignInAsync();

            // One trip with both people, one with only Amelia.
            var bothSlug = await CreateTripAsync(castle: false, people: new[] { "Amelia", "Gary" }, lat: "22.0", lng: "22.0");
            var ameliaOnlySlug = await CreateTripAsync(castle: false, people: new[] { "Amelia" }, lat: "44.0", lng: "44.0");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                // Selecting Amelia AND Gary must keep only the trip that has both —
                // the Amelia-only trip drops out (AND, not OR).
                await Page.ClickAsync("label.chip:has(input[value='Amelia'])");
                await Page.ClickAsync("label.chip:has(input[value='Gary'])");

                await Expect(CardFor(bothSlug)).ToBeVisibleAsync();
                await Expect(CardFor(ameliaOnlySlug)).ToBeHiddenAsync();
            }
            finally
            {
                await DeleteAlbumAsync(bothSlug);
                await DeleteAlbumAsync(ameliaOnlySlug);
            }
        }

        [Test]
        public async Task Exact_match_shows_only_trips_with_exactly_the_selected_people()
        {
            await SignInAsync();

            var soloSlug = await CreateTripAsync(castle: false, people: new[] { "Gary" }, lat: "24.0", lng: "24.0");
            var familySlug = await CreateTripAsync(castle: false, people: new[] { "Gary", "Amelia" }, lat: "48.0", lng: "48.0");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                // Selecting Gary alone (AND) keeps both trips — both include Gary.
                await Page.ClickAsync("label.chip:has(input[value='Gary'])");
                await Expect(CardFor(soloSlug)).ToBeVisibleAsync();
                await Expect(CardFor(familySlug)).ToBeVisibleAsync();

                // Turning on "Exact match" keeps only the trip whose people are
                // exactly {Gary}; the family trip has an extra person and drops out.
                await Page.CheckAsync(".filter-exact");
                await Expect(CardFor(soloSlug)).ToBeVisibleAsync();
                await Expect(CardFor(familySlug)).ToBeHiddenAsync();
            }
            finally
            {
                await DeleteAlbumAsync(soloSlug);
                await DeleteAlbumAsync(familySlug);
            }
        }

        [Test]
        public async Task No_matching_trips_shows_an_empty_message()
        {
            await SignInAsync();

            var slug = await CreateTripAsync(castle: false, people: new[] { "Amelia", "Gary" }, lat: "26.0", lng: "26.0");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                // Amelia + Exact excludes the {Amelia, Gary} trip (extra person) and
                // the sample (no people), so nothing matches.
                await Page.ClickAsync("label.chip:has(input[value='Amelia'])");
                await Page.CheckAsync(".filter-exact");

                await Expect(Page.Locator("#tripsEmpty")).ToBeVisibleAsync();
                await Expect(Page.Locator(".trips .section-head__count")).ToHaveTextAsync("0 trips");
            }
            finally
            {
                await DeleteAlbumAsync(slug);
            }
        }

        [Test]
        public async Task Clearing_the_filters_restores_every_trip()
        {
            await SignInAsync();

            var castleSlug = await CreateTripAsync(castle: true, people: System.Array.Empty<string>(), lat: "15.0", lng: "15.0");

            try
            {
                await Page.GotoAsync(BaseUrl + "/");

                // Filter to castles: the seeded (non-castle) sample trip drops out.
                await Page.ClickAsync("label.chip:has(.filter-castle)");
                await Expect(CardFor(ServerFixture.SampleAlbumSlug)).ToBeHiddenAsync();

                await Page.ClickAsync(".filters__clear");
                await Expect(CardFor(ServerFixture.SampleAlbumSlug)).ToBeVisibleAsync();
                await Expect(CardFor(castleSlug)).ToBeVisibleAsync();
            }
            finally
            {
                await DeleteAlbumAsync(castleSlug);
            }
        }

        private ILocator CardFor(string slug)
        {
            return Page.Locator($".trip-card[href='/album/{slug}/']");
        }

        /// <summary>
        /// Creates a throwaway trip via the home-page "Add trip" modal with the given
        /// castle flag, people and coordinates, and returns its slug.
        /// </summary>
        /// <param name="castle">Whether to tick "castle visited".</param>
        /// <param name="people">The people checkboxes to tick.</param>
        /// <param name="lat">The latitude to record.</param>
        /// <param name="lng">The longitude to record.</param>
        /// <returns>The created album's slug.</returns>
        private async Task<string> CreateTripAsync(bool castle, string[] people, string lat, string lng)
        {
            await Page.GotoAsync(BaseUrl + "/");
            await OpenAddTripModalAsync();

            await Page.FillAsync("#name", "Filter " + System.Guid.NewGuid().ToString("N"));
            await Page.FillAsync("#visited", "2026-06-06");
            await Page.FillAsync("#latitude", lat);
            await Page.FillAsync("#longitude", lng);

            if (castle)
            {
                await Page.CheckAsync("#castleVisited");
            }

            foreach (var person in people)
            {
                await Page.CheckAsync($"input[name='people'][value='{person}']");
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
    }
}