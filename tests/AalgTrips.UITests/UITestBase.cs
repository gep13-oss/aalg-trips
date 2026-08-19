using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Shared base for the Playwright UI tests. Centralises the pieces the
    /// admin-facing tests all need: the server base URL, the sign-in flow, and the
    /// antiforgery-token / form-POST helpers used to drive the mutation endpoints
    /// through <c>Page.APIRequest</c>. Tests that only read the public site
    /// (for example the login and public-site suites) can extend
    /// <see cref="PageTest"/> directly instead.
    /// </summary>
    public abstract class UITestBase : PageTest
    {
        protected static string BaseUrl => ServerFixture.BaseUrl;

        /// <summary>
        /// Signs in as the seeded admin (the legacy <c>user</c> account) and waits
        /// for the redirect home. After this the browser context holds the
        /// authenticated cookie, which the context's <c>Page.APIRequest</c> shares.
        /// </summary>
        /// <returns>A task that completes once sign-in has landed on the home page.</returns>
        protected Task SignInAsync()
        {
            return SignInAsAsync(ServerFixture.TestUsername, ServerFixture.TestPassword);
        }

        /// <summary>
        /// Signs in as the seeded viewer-role account, for tests that check a viewer
        /// can browse but not manage content.
        /// </summary>
        /// <returns>A task that completes once sign-in has landed on the home page.</returns>
        protected Task SignInAsViewerAsync()
        {
            return SignInAsAsync(ServerFixture.TestViewerUsername, ServerFixture.TestViewerPassword);
        }

        /// <summary>
        /// Signs in as a specific user and waits for the redirect home.
        /// </summary>
        /// <param name="username">The username to sign in with.</param>
        /// <param name="password">The password to sign in with.</param>
        /// <returns>A task that completes once sign-in has landed on the home page.</returns>
        protected async Task SignInAsAsync(string username, string password)
        {
            await Page.GotoAsync(BaseUrl + "/login");
            await Page.FillAsync("#username", username);
            await Page.FillAsync("#password", password);
            await Page.ClickAsync("input[type=submit]");
            await Page.WaitForURLAsync(BaseUrl + "/");
        }

        /// <summary>
        /// Opens the home page "Add trip" modal and waits for its dialog to be open.
        /// The create form now lives inside a &lt;dialog&gt; behind this button, so a
        /// test that fills the create fields must open the modal first.
        /// </summary>
        /// <returns>A task that completes once the Add-trip dialog is open.</returns>
        protected async Task OpenAddTripModalAsync()
        {
            await Page.ClickAsync("[data-open-dialog='#addTripDialog']");
            await Page.WaitForSelectorAsync("#addTripDialog[open]");
        }

        /// <summary>
        /// Opens the album page's "Actions" dropdown so its items (Edit, Rename,
        /// Upload, Delete) become visible and actionable.
        /// </summary>
        /// <returns>A task that completes once the dropdown is open.</returns>
        protected async Task OpenActionsMenuAsync()
        {
            await Page.ClickAsync("summary.actions-menu__trigger");
            await Page.WaitForSelectorAsync(".actions-menu[open]");
        }

        /// <summary>
        /// Opens a named action modal from the album page's "Actions" dropdown: it
        /// opens the menu, clicks the matching item and waits for that dialog to open.
        /// </summary>
        /// <param name="dialogId">The dialog element id, for example "editDialog".</param>
        /// <returns>A task that completes once the requested dialog is open.</returns>
        protected async Task OpenAlbumActionAsync(string dialogId)
        {
            await OpenActionsMenuAsync();
            await Page.ClickAsync($"[data-open-dialog='#{dialogId}']");
            await Page.WaitForSelectorAsync($"#{dialogId}[open]");
        }

        /// <summary>
        /// Opens the home page "Add cruise" modal and waits for its dialog to be open.
        /// </summary>
        /// <returns>A task that completes once the Add-cruise dialog is open.</returns>
        protected async Task OpenAddCruiseModalAsync()
        {
            await Page.ClickAsync("[data-open-dialog='#addCruiseDialog']");
            await Page.WaitForSelectorAsync("#addCruiseDialog[open]");
        }

        /// <summary>
        /// Creates a cruise through the home-page "Add cruise" modal and waits for the
        /// redirect to its detail page, returning the slug the create assigned.
        /// </summary>
        /// <param name="title">The cruise title to create.</param>
        /// <param name="startDate">The departure date (yyyy-MM-dd).</param>
        /// <param name="endDate">The return date (yyyy-MM-dd).</param>
        /// <returns>The slug of the created cruise.</returns>
        protected async Task<string> CreateCruiseAsync(string title, string startDate = "2025-07-15", string endDate = "2025-07-22")
        {
            await Page.GotoAsync(BaseUrl + "/");
            await OpenAddCruiseModalAsync();
            await Page.FillAsync("#cruiseName", title);
            await Page.FillAsync("#cruiseStart", startDate);
            await Page.FillAsync("#cruiseEnd", endDate);
            await Page.ClickAsync("#newcruise");
            await Page.WaitForURLAsync(new Regex("/cruise/[^/]+/$"));

            return Regex.Match(Page.Url, "/cruise/([^/]+)/").Groups[1].Value;
        }

        /// <summary>
        /// Deletes a cruise through its detail-page Actions menu, accepting the delete
        /// confirmation, so a mutation test can clean up after itself. A no-op when the
        /// cruise no longer exists.
        /// </summary>
        /// <param name="slug">The slug of the cruise to delete.</param>
        /// <returns>A task that completes once the cruise has been deleted.</returns>
        protected async Task DeleteCruiseAsync(string slug)
        {
            var exists = await Page.APIRequest.GetAsync($"{BaseUrl}/cruise/{slug}/");

            if (exists.Status != 200)
            {
                return;
            }

            await Page.GotoAsync($"{BaseUrl}/cruise/{slug}/");
            await Page.ClickAsync("summary.actions-menu__trigger");
            await Page.WaitForSelectorAsync(".actions-menu[open]");

            Page.Dialog += AcceptDialog;
            await Page.ClickAsync("#deletecruise");
            await Page.WaitForURLAsync(BaseUrl + "/");
            Page.Dialog -= AcceptDialog;
        }

        /// <summary>
        /// Accepts a native dialog (the delete <c>confirm()</c>), so Playwright does
        /// not auto-dismiss it and cancel the action.
        /// </summary>
        /// <param name="sender">The event source.</param>
        /// <param name="dialog">The dialog to accept.</param>
        protected static void AcceptDialog(object? sender, IDialog dialog)
        {
            dialog.AcceptAsync();
        }

        /// <summary>
        /// Reads a valid antiforgery token from a page that renders one of the admin
        /// forms. The token is scoped to the current context (anonymous before
        /// <see cref="SignInAsync"/>, authenticated after) and <c>Page.APIRequest</c>
        /// shares the cookie jar, so a POST built with it is antiforgery-valid.
        /// </summary>
        /// <param name="path">The page to read the token from; defaults to home.</param>
        /// <returns>The antiforgery token value.</returns>
        protected async Task<string> AntiforgeryTokenAsync(string path = "/")
        {
            await Page.GotoAsync(BaseUrl + path);
            var token = await Page.GetAttributeAsync("input[name='__RequestVerificationToken']", "value");
            return token!;
        }

        /// <summary>
        /// Builds form-urlencoded POST options carrying the antiforgery token and the
        /// given fields, with redirects disabled so the raw status code is visible.
        /// </summary>
        /// <param name="token">The antiforgery token to include.</param>
        /// <param name="fields">Additional form fields to send.</param>
        /// <returns>Options ready to pass to <c>Page.APIRequest.PostAsync</c>.</returns>
        protected static APIRequestContextOptions FormPost(string token, params (string Key, string Value)[] fields)
        {
            var body = "__RequestVerificationToken=" + Uri.EscapeDataString(token);

            foreach (var (key, value) in fields)
            {
                body += "&" + Uri.EscapeDataString(key) + "=" + Uri.EscapeDataString(value);
            }

            return new APIRequestContextOptions
            {
                MaxRedirects = 0,
                Headers = new Dictionary<string, string>
                {
                    ["content-type"] = "application/x-www-form-urlencoded",
                },
                Data = body,
            };
        }
    }
}