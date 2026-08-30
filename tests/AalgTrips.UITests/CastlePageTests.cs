using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Coverage for the "Castle Bingo" page (/castles): it is behind the site login,
    /// renders the scoreboard, country/access filters and the operator glossary,
    /// defaults to hiding the bare ruins, and marks a castle as visited (with a link
    /// to the album) when a castle-flagged trip sits on it. Each mutation test creates
    /// a throwaway album on a real castle and deletes it again.
    /// </summary>
    [TestFixture]
    public class CastlePageTests : UITestBase
    {
        // Tolquhon Castle, ~5 miles from Ellon: near enough to sit on the first page of
        // the nearest-first grid, and a real, visitable castle in the catalogue.
        private const string TolquhonName = "Tolquhon Castle";
        private const string TolquhonLat = "57.34814";
        private const string TolquhonLong = "-2.21321";

        [Test]
        public async Task Anonymous_visitor_is_sent_to_login()
        {
            await Page.GotoAsync(BaseUrl + "/castles");

            await Expect(Page).ToHaveURLAsync(new Regex("/login"));
        }

        [Test]
        public async Task Page_shows_the_scoreboard_filters_and_operator_glossary()
        {
            await SignInAsync();
            await Page.GotoAsync(BaseUrl + "/castles");

            await Expect(Page.Locator(".castle-score")).ToBeVisibleAsync();

            // The country filter offers a Scotland chip.
            await Expect(Page.Locator(".filter-nation[value='Scotland']")).ToHaveCountAsync(1);

            // The access filter is present.
            await Expect(Page.Locator(".filter-access").First).ToBeVisibleAsync();

            // The abbreviations are spelled out for the reader.
            await Expect(Page.Locator(".castle-legend__glossary")).ToContainTextAsync("Historic Environment Scotland");
            await Expect(Page.Locator(".castle-legend__glossary")).ToContainTextAsync("National Trust for Scotland");
        }

        [Test]
        public async Task Bare_ruins_are_hidden_until_the_toggle_is_ticked()
        {
            await SignInAsync();
            await Page.GotoAsync(BaseUrl + "/castles");

            // The catalogue includes many non-visitable fragments; they are in the DOM
            // but hidden by default.
            var ruin = Page.Locator(".castle-card[data-visitable='false']").First;
            await Expect(ruin).ToHaveCountAsync(1);
            await Expect(ruin).ToBeHiddenAsync();

            // Ticking "show ruins" reveals them.
            await Page.CheckAsync(".filter-ruins");
            await Expect(ruin).ToBeVisibleAsync();
        }

        [Test]
        public async Task A_castle_trip_marks_its_castle_visited_and_links_the_album()
        {
            await SignInAsync();

            var slug = await CreateCastleTripAsync(TolquhonName, TolquhonLat, TolquhonLong);

            try
            {
                await Page.GotoAsync(BaseUrl + "/castles");

                var card = Page.Locator(".castle-card").Filter(new() { HasText = TolquhonName });

                await Expect(card).ToHaveAttributeAsync("data-visited", "true");
                await Expect(card.Locator($"a[href='/album/{slug}/']")).ToHaveCountAsync(1);
            }
            finally
            {
                await DeleteAlbumAsync(slug);
            }
        }

        [Test]
        public async Task Admin_can_mark_a_castle_visited_and_unmark_it()
        {
            await SignInAsync();
            await Page.GotoAsync(BaseUrl + "/castles");

            // Castle Fraser: a near, visitable castle with no album in the test seed,
            // so it starts unvisited and is on the first page of the grid.
            var castleId = await Card("Castle Fraser").GetAttributeAsync("data-id");

            try
            {
                await Expect(Card("Castle Fraser")).ToHaveAttributeAsync("data-visited", "false");

                await Card("Castle Fraser").Locator("[data-castle-action='mark']").ClickAsync();
                await Expect(Card("Castle Fraser")).ToHaveAttributeAsync("data-visited", "true");

                await Card("Castle Fraser").Locator("[data-castle-action='unmark']").ClickAsync();
                await Expect(Card("Castle Fraser")).ToHaveAttributeAsync("data-visited", "false");
            }
            finally
            {
                // Safety net so a mid-test failure cannot leave the shared server with
                // the castle still ticked.
                var token = await AntiforgeryTokenAsync("/castles");
                await Page.APIRequest.PostAsync($"{BaseUrl}/castles?handler=Unmark", FormPost(token, ("castleId", castleId ?? string.Empty)));
            }
        }

        [Test]
        public async Task Add_album_button_prefills_the_castle_details()
        {
            await SignInAsync();
            await Page.GotoAsync(BaseUrl + "/castles");

            var name = await Card("Castle Fraser").GetAttributeAsync("data-name");

            await Card("Castle Fraser").Locator("[data-castle-create]").ClickAsync();

            await Expect(Page.Locator("#castleAlbumDialog")).ToBeVisibleAsync();
            await Expect(Page.Locator("#castleAlbumName")).ToHaveValueAsync(name ?? string.Empty);
            await Expect(Page.Locator("#castleAlbumLat")).Not.ToHaveValueAsync(string.Empty);
            await Expect(Page.Locator("#castleAlbumDialog input[name='castleVisited']")).ToHaveValueAsync("true");
        }

        private Microsoft.Playwright.ILocator Card(string castleName)
        {
            return Page.Locator(".castle-card").Filter(new() { HasText = castleName });
        }

        private async Task<string> CreateCastleTripAsync(string name, string lat, string lon)
        {
            await Page.GotoAsync(BaseUrl + "/");
            await OpenAddTripModalAsync();

            await Page.FillAsync("#name", name + " " + System.Guid.NewGuid().ToString("N"));
            await Page.FillAsync("#visited", "2026-05-05");
            await Page.FillAsync("#latitude", lat);
            await Page.FillAsync("#longitude", lon);
            await Page.CheckAsync("#castleVisited");
            await Page.ClickAsync("#newalbum");
            await Page.WaitForURLAsync(new Regex("/album/[^/]+/$"));

            return Regex.Match(Page.Url, "/album/([^/]+)/$").Groups[1].Value;
        }

        private async Task DeleteAlbumAsync(string slug)
        {
            var token = await AntiforgeryTokenAsync();
            await Page.APIRequest.PostAsync($"{BaseUrl}/album/{slug}/delete", FormPost(token));
        }
    }
}