using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Verifies the site footer shows which build is live. The commit sha the app
    /// was built from is stamped into the assembly's informational version and read
    /// back by <c>BuildInfo</c>; the footer renders its short form (or "dev" for an
    /// un-stamped local build). This keeps the wiring honest without asserting a
    /// specific sha, which changes every commit.
    /// </summary>
    [TestFixture]
    public class FooterBuildInfoTests : UITestBase
    {
        [Test]
        public async Task Footer_shows_the_build_revision()
        {
            // The footer is on every page, including the public login page — no sign-in needed.
            await Page.GotoAsync(BaseUrl + "/login");

            var build = Page.Locator(".site-footer__build");
            await Expect(build).ToHaveCountAsync(1);

            // A seven-character short sha for a stamped build, or "dev" when none was stamped.
            await Expect(build).ToHaveTextAsync(new Regex("^([0-9a-f]{7}|dev)$"));

            // The full sha (or a development-build note) is surfaced on hover.
            await Expect(build).ToHaveAttributeAsync("title", new Regex(@"^(Built from commit [0-9a-f]+|Local development build)$"));

            // A stamped build links its sha to the commit on GitHub.
            if (await build.TextContentAsync() != "dev")
            {
                await Expect(build).ToHaveAttributeAsync(
                    "href", new Regex(@"^https://github\.com/gep13-oss/aalg-trips/commit/[0-9a-f]{40}$"));
            }
        }
    }
}