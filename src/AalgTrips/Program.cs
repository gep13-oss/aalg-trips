using System;
using System.IO;
using AalgTrips.Models;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

// ---- Services (was Startup.ConfigureServices) ----
builder.Services.AddRazorPages();

// Photos (especially from phones) are several MB each, so a batch upload easily
// exceeds Kestrel's 30 MB default request-body limit — which surfaces as an
// antiforgery failure / 400 because the oversized body can't be read. Raise it
// for both Kestrel and multipart form parsing, configurable via
// Upload:MaxRequestBodyBytes. The whole site is behind authentication, so only a
// signed-in user can post a large body.
long maxUploadBytes = builder.Configuration.GetValue<long?>("Upload:MaxRequestBodyBytes") ?? 104_857_600L;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxUploadBytes);
builder.Services.Configure<FormOptions>(options => options.MultipartBodyLengthLimit = maxUploadBytes);

// Album content (photos, thumbnails, metadata, markers) is read and written
// through an IPhotoStore, selected by configuration ("Storage:Provider"). The
// default is the local disk store used in development and tests; production
// selects the Azure Blob store so content survives redeploys. Content is never
// a public static file — it is streamed through the authenticated /albums media
// endpoint below — so the local store keeps its content outside the web root
// and the Azure container is private.
if (string.Equals(builder.Configuration["Storage:Provider"], "AzureBlob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IPhotoStore>(sp =>
    {
        var configuration = sp.GetRequiredService<IConfiguration>();
        string connectionString = configuration["Storage:AzureBlob:ConnectionString"];
        string containerName = configuration["Storage:AzureBlob:ContainerName"];

        return new AzureBlobPhotoStore(
            connectionString,
            string.IsNullOrWhiteSpace(containerName) ? "albums" : containerName);
    });
}
else
{
    builder.Services.AddSingleton<IPhotoStore>(sp =>
    {
        var environment = sp.GetRequiredService<IWebHostEnvironment>();

        // Album content lives under the content root (App_Data), NOT the web root,
        // so the static-file middleware never serves it: the only way to reach a
        // photo is the authenticated media endpoint.
        string albumsRoot = Path.Combine(environment.ContentRootPath, "App_Data", "albums");
        return new LocalDiskPhotoStore(albumsRoot);
    });
}

builder.Services.AddSingleton<AlbumCollection>();
builder.Services.AddSingleton<JourneyCollection>();
builder.Services.AddSingleton<ImageProcessor>();
builder.Services.AddSingleton<UserAuthenticator>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
}).AddCookie(options =>
{
    // The admin handlers challenge unauthenticated requests; without these the
    // cookie handler would redirect to its default /Account/Login, which does
    // not exist here. The login page lives at /login and reads returnUrl.
    options.LoginPath = "/login";

    // A signed-in viewer who is denied an admin-only action is already past the
    // login page, so send them home rather than back to /login.
    options.AccessDeniedPath = "/";
    options.ReturnUrlParameter = "returnUrl";
});

// The whole site requires a signed-in user: a fallback policy applies to every
// endpoint that does not opt out. Only the login page is marked [AllowAnonymous]
// (and the static assets it needs are served before routing, so they stay
// public). This is what gates the map, albums and photos behind login.
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

// Security response headers (OWASP Secure Headers Project) applied to EVERY
// response — static files, the authenticated media endpoint, Razor pages and
// error responses alike — by sitting at the front of the pipeline. web.config's
// header block is an IIS concept and is ignored on Linux App Service, so the
// headers are set here instead. The CSP was rolled out in Report-Only first and
// is now enforced: the site loads no inline scripts and only OpenStreetMap tiles
// cross-origin, so script-src can stay 'self' (style-src keeps 'unsafe-inline'
// because Leaflet/PhotoSwipe set inline styles).
const string contentSecurityPolicy =
    "default-src 'self'; base-uri 'self'; object-src 'none'; frame-ancestors 'none'; " +
    "form-action 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
    "img-src 'self' data: https://tile.openstreetmap.org; font-src 'self'; " +
    "connect-src 'self'; upgrade-insecure-requests";

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Content-Security-Policy"] = contentSecurityPolicy;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["X-Frame-Options"] = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // OWASP now recommends explicitly disabling the legacy, deprecated XSS filter.
    headers["X-XSS-Protection"] = "0";
    headers["Cross-Origin-Opener-Policy"] = "same-origin";
    headers["Cross-Origin-Resource-Policy"] = "same-origin";
    headers["Permissions-Policy"] =
        "geolocation=(), camera=(), microphone=(), payment=(), usb=(), display-capture=(), fullscreen=(self)";

    // Only meaningful over a secure connection; Cloudflare also enforces HSTS at
    // the edge, so this is the origin-side belt to that braces.
    if (context.Request.IsHttps)
    {
        headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }

    await next();
});

// ---- HTTP pipeline (was Startup.Configure) ----
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseStatusCodePages("text/plain", "Status code page, status code: {0}");

app.UseStaticFiles(new StaticFileOptions()
{
    OnPrepareResponse = (context) =>
    {
        var time = TimeSpan.FromDays(365);
        context.Context.Response.Headers[HeaderNames.CacheControl] = $"max-age={time.TotalSeconds.ToString()}";
        context.Context.Response.Headers[HeaderNames.Expires] = DateTime.UtcNow.Add(time).ToString("R");
    },
});

if (app.Configuration.GetValue<bool>("forcessl"))
{
    app.UseRewriter(new RewriteOptions().AddRedirectToHttps());
}

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Authenticated media endpoint. Album photos, thumbnails and the marker file
// are private (not static files); they are streamed from the photo store only
// for a signed-in user. The catch-all key is the store key, e.g.
// "sample-trip/beach.jpg" or "sample-trip/thumbnail/beach-190x127.jpg".
app.MapGet("/albums/{**key}", (string key, IPhotoStore store, HttpContext http) =>
{
    if (string.IsNullOrEmpty(key) || !store.TryOpenContent(key, out var content))
    {
        return Results.NotFound();
    }

    // Photos and thumbnails are immutable under a given key (a new photo always
    // takes a new name), so they can be cached hard. markers.json and journeys.json
    // are the exception: they are rewritten on every album/journey create/edit/
    // delete, so a long cache would leave the map showing a stale set — revalidate
    // them instead.
    bool isGeneratedIndex = key.Equals(PhotoStoreConventions.MarkersFileName, StringComparison.OrdinalIgnoreCase)
        || key.Equals(PhotoStoreConventions.JourneysFileName, StringComparison.OrdinalIgnoreCase);
    http.Response.Headers[HeaderNames.CacheControl] = isGeneratedIndex ? "no-cache" : "private, max-age=86400";
    return Results.Stream(content, ContentTypeForKey(key));
}).RequireAuthorization();

// Rebuild the map's marker file from the current album set on startup, so it
// self-heals if it ever drifts from the albums (for example if content is changed
// directly in the store) and picks up any marker-schema change after a deploy.
// A transient store failure must not stop the app from booting.
try
{
    await app.Services.GetRequiredService<AlbumCollection>().WriteMarkersAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not rebuild markers.json on startup.");
}

// Rebuild the map's journey-route file from the current journey set on startup, for
// the same self-healing reason as the markers above. Kept in its own try so a
// journey-side failure cannot stop the albums' markers from being written.
try
{
    await app.Services.GetRequiredService<JourneyCollection>().WriteJourneysAsync();
}
catch (Exception ex)
{
    app.Logger.LogWarning(ex, "Could not rebuild journeys.json on startup.");
}

app.Run();

static string ContentTypeForKey(string key)
{
    string extension = Path.GetExtension(key).ToLowerInvariant();
    return extension switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".json" => "application/json",
        _ => "application/octet-stream",
    };
}