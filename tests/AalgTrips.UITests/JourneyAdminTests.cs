using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// End-to-end coverage for the journey admin CRUD: an admin can create, edit,
    /// rename and delete a journey through the modals, and a title that slugs to an
    /// existing journey is rejected with 409 rather than overwriting it. Each mutation
    /// test uses a unique title and deletes the journey it created so the suite stays
    /// self-contained.
    /// </summary>
    [TestFixture]
    public class JourneyAdminTests : UITestBase
    {
        [Test]
        public async Task Authenticated_admin_can_create_a_journey()
        {
            await SignInAsync();

            var title = "Create Journey " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateJourneyAsync(title);

            try
            {
                var detail = await Page.APIRequest.GetAsync($"{BaseUrl}/journey/{slug}/");
                Assert.That(detail.Status, Is.EqualTo(200), "the created journey should be reachable at its slug");

                var content = await Page.ContentAsync();
                Assert.That(content, Does.Contain(title), "the journey page should show its title");
            }
            finally
            {
                await DeleteJourneyAsync(slug);
            }
        }

        [Test]
        public async Task Creating_a_journey_whose_name_collides_is_rejected()
        {
            await SignInAsync();

            var title = "Collision Journey " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateJourneyAsync(title);

            try
            {
                // A second journey whose title slugs to the same value must be refused,
                // not written over the first.
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddJourneyModalAsync();
                await Page.FillAsync("#journeyName", title);
                await Page.FillAsync("#journeyStart", "2025-07-15");
                await Page.FillAsync("#journeyEnd", "2025-07-22");

                var response = await Page.RunAndWaitForResponseAsync(
                    () => Page.ClickAsync("#newjourney"),
                    r => r.Request.Method == "POST" && r.Url.Contains("/journey/new/create"));

                Assert.That(response.Status, Is.EqualTo(409), "a duplicate-slug create should be rejected with 409 Conflict");

                var existing = await Page.APIRequest.GetAsync($"{BaseUrl}/journey/{slug}/");
                Assert.That(existing.Status, Is.EqualTo(200), "the existing journey should be untouched by the rejected create");
            }
            finally
            {
                await DeleteJourneyAsync(slug);
            }
        }

        [Test]
        public async Task Editing_a_journey_updates_its_details()
        {
            await SignInAsync();

            var title = "Edit Journey " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateJourneyAsync(title);

            try
            {
                await Page.GotoAsync($"{BaseUrl}/journey/{slug}/");
                await OpenActionsMenuAsync();
                await Page.ClickAsync("[data-open-dialog='#editJourneyDialog']");
                await Page.WaitForSelectorAsync("#editJourneyDialog[open]");

                var newDescription = "Edited by JourneyAdminTests " + System.Guid.NewGuid().ToString("N");
                await Page.FillAsync("#journeyDescription", newDescription);
                await Page.ClickAsync("#btnEditJourney");

                await Page.WaitForURLAsync(new Regex($"/journey/{slug}/$"));

                var content = await Page.ContentAsync();
                Assert.That(content, Does.Contain(newDescription), "the journey page should show the edited notes");
            }
            finally
            {
                await DeleteJourneyAsync(slug);
            }
        }

        [Test]
        public async Task Renaming_a_journey_changes_its_slug_and_url()
        {
            await SignInAsync();

            var title = "Rename Journey " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateJourneyAsync(title);

            try
            {
                await Page.GotoAsync($"{BaseUrl}/journey/{slug}/");
                await OpenActionsMenuAsync();
                await Page.ClickAsync("[data-open-dialog='#renameJourneyDialog']");
                await Page.WaitForSelectorAsync("#renameJourneyDialog[open]");

                await Page.FillAsync("#renameJourneyName", "Renamed Journey " + System.Guid.NewGuid().ToString("N"));
                await Page.ClickAsync("#btnRenameJourney");

                await Page.WaitForURLAsync(new Regex("/journey/[^/]+/$"));
                var renamedSlug = Regex.Match(Page.Url, "/journey/([^/]+)/").Groups[1].Value;

                var oldUrl = await Page.APIRequest.GetAsync($"{BaseUrl}/journey/{slug}/");
                var newUrl = await Page.APIRequest.GetAsync($"{BaseUrl}/journey/{renamedSlug}/");
                Assert.Multiple(() =>
                {
                    Assert.That(oldUrl.Status, Is.EqualTo(404), "the old slug should no longer resolve");
                    Assert.That(newUrl.Status, Is.EqualTo(200), "the journey should be reachable under the new slug");
                });

                // Clean up the renamed journey rather than the original.
                slug = renamedSlug;
            }
            finally
            {
                await DeleteJourneyAsync(slug);
            }
        }

        [Test]
        public async Task Deleting_a_journey_removes_it()
        {
            await SignInAsync();

            var title = "Delete Journey " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateJourneyAsync(title);

            await DeleteJourneyAsync(slug);

            var response = await Page.APIRequest.GetAsync($"{BaseUrl}/journey/{slug}/");
            Assert.That(response.Status, Is.EqualTo(404), "the deleted journey should no longer resolve");
        }
    }
}