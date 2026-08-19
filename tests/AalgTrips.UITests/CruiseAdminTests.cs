using System.Text.RegularExpressions;

namespace AalgTrips.UITests
{
    /// <summary>
    /// End-to-end coverage for the cruise admin CRUD: an admin can create, edit,
    /// rename and delete a cruise through the modals, and a title that slugs to an
    /// existing cruise is rejected with 409 rather than overwriting it. Each mutation
    /// test uses a unique title and deletes the cruise it created so the suite stays
    /// self-contained.
    /// </summary>
    [TestFixture]
    public class CruiseAdminTests : UITestBase
    {
        [Test]
        public async Task Authenticated_admin_can_create_a_cruise()
        {
            await SignInAsync();

            var title = "Create Cruise " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateCruiseAsync(title);

            try
            {
                var detail = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{slug}/");
                Assert.That(detail.Status, Is.EqualTo(200), "the created cruise should be reachable at its slug");

                var content = await Page.ContentAsync();
                Assert.That(content, Does.Contain(title), "the cruise page should show its title");
            }
            finally
            {
                await DeleteCruiseAsync(slug);
            }
        }

        [Test]
        public async Task Creating_a_cruise_whose_name_collides_is_rejected()
        {
            await SignInAsync();

            var title = "Collision Cruise " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateCruiseAsync(title);

            try
            {
                // A second cruise whose title slugs to the same value must be refused,
                // not written over the first.
                await Page.GotoAsync(BaseUrl + "/");
                await OpenAddCruiseModalAsync();
                await Page.FillAsync("#cruiseName", title);
                await Page.FillAsync("#cruiseStart", "2025-07-15");
                await Page.FillAsync("#cruiseEnd", "2025-07-22");

                var response = await Page.RunAndWaitForResponseAsync(
                    () => Page.ClickAsync("#newcruise"),
                    r => r.Request.Method == "POST" && r.Url.Contains("/cruise/new/create"));

                Assert.That(response.Status, Is.EqualTo(409), "a duplicate-slug create should be rejected with 409 Conflict");

                var existing = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{slug}/");
                Assert.That(existing.Status, Is.EqualTo(200), "the existing cruise should be untouched by the rejected create");
            }
            finally
            {
                await DeleteCruiseAsync(slug);
            }
        }

        [Test]
        public async Task Editing_a_cruise_updates_its_details()
        {
            await SignInAsync();

            var title = "Edit Cruise " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateCruiseAsync(title);

            try
            {
                await Page.GotoAsync($"{BaseUrl}/cruise/{slug}/");
                await OpenActionsMenuAsync();
                await Page.ClickAsync("[data-open-dialog='#editCruiseDialog']");
                await Page.WaitForSelectorAsync("#editCruiseDialog[open]");

                var newDescription = "Edited by CruiseAdminTests " + System.Guid.NewGuid().ToString("N");
                await Page.FillAsync("#cruiseDescription", newDescription);
                await Page.ClickAsync("#btnEditCruise");

                await Page.WaitForURLAsync(new Regex($"/cruise/{slug}/$"));

                var content = await Page.ContentAsync();
                Assert.That(content, Does.Contain(newDescription), "the cruise page should show the edited notes");
            }
            finally
            {
                await DeleteCruiseAsync(slug);
            }
        }

        [Test]
        public async Task Renaming_a_cruise_changes_its_slug_and_url()
        {
            await SignInAsync();

            var title = "Rename Cruise " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateCruiseAsync(title);

            try
            {
                await Page.GotoAsync($"{BaseUrl}/cruise/{slug}/");
                await OpenActionsMenuAsync();
                await Page.ClickAsync("[data-open-dialog='#renameCruiseDialog']");
                await Page.WaitForSelectorAsync("#renameCruiseDialog[open]");

                await Page.FillAsync("#renameCruiseName", "Renamed Cruise " + System.Guid.NewGuid().ToString("N"));
                await Page.ClickAsync("#btnRenameCruise");

                await Page.WaitForURLAsync(new Regex("/cruise/[^/]+/$"));
                var renamedSlug = Regex.Match(Page.Url, "/cruise/([^/]+)/").Groups[1].Value;

                var oldUrl = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{slug}/");
                var newUrl = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{renamedSlug}/");
                Assert.Multiple(() =>
                {
                    Assert.That(oldUrl.Status, Is.EqualTo(404), "the old slug should no longer resolve");
                    Assert.That(newUrl.Status, Is.EqualTo(200), "the cruise should be reachable under the new slug");
                });

                // Clean up the renamed cruise rather than the original.
                slug = renamedSlug;
            }
            finally
            {
                await DeleteCruiseAsync(slug);
            }
        }

        [Test]
        public async Task Deleting_a_cruise_removes_it()
        {
            await SignInAsync();

            var title = "Delete Cruise " + System.Guid.NewGuid().ToString("N");
            var slug = await CreateCruiseAsync(title);

            await DeleteCruiseAsync(slug);

            var response = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{slug}/");
            Assert.That(response.Status, Is.EqualTo(404), "the deleted cruise should no longer resolve");
        }
    }
}