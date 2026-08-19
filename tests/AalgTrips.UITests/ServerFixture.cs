using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AalgTrips.UITests
{
    /// <summary>
    /// Boots the migrated aalg-trips app once for the whole test run and tears it
    /// down afterwards. The app is launched as a child process against a private,
    /// seeded web root and with injected test credentials, so the suite is fully
    /// self-contained: `dotnet test` (or the Cake `Test` target) needs nothing
    /// running beforehand. Set AALG_TRIPS_BASEURL to point the suite at an already
    /// running instance instead (e.g. a deployed environment).
    /// </summary>
    [SetUpFixture]
    public sealed class ServerFixture
    {
        private static Process? _app;
        private static string? _tempContentRoot;

        public static string BaseUrl { get; private set; } = string.Empty;

        public const string TestUsername = "testuser";
        public const string TestPassword = "test-password-123";
        public const string TestSalt = "test-salt";

        // A second, viewer-role account (the legacy "user" above is the admin), so
        // the tests can exercise the viewer-can-look-but-not-manage behaviour.
        public const string TestViewerUsername = "testviewer";
        public const string TestViewerPassword = "viewer-password-123";
        public const string SampleAlbumSlug = "sample-trip";
        public const string SampleAlbumTitle = "Sample Trip";
        public const string SampleCruiseSlug = "sample-cruise";
        public const string SampleCruiseTitle = "Sample Cruise";

        [OneTimeSetUp]
        public async Task StartServer()
        {
            var external = Environment.GetEnvironmentVariable("AALG_TRIPS_BASEURL");
            if (!string.IsNullOrWhiteSpace(external))
            {
                BaseUrl = external.TrimEnd('/');
                return;
            }

            var webProject = FindWebProject();
            var webDir = Path.GetDirectoryName(webProject)!;

            // Run the app against a fully isolated content root: its web root is
            // {contentRoot}/wwwroot, seeded below. Pointing --webroot at a directory
            // outside the content root does NOT reliably redirect static-file
            // serving under WebApplication (the file provider and WebRootPath can
            // diverge), so the app would otherwise serve the developer's real
            // src/AalgTrips/wwwroot/albums instead of the seed. Isolating the whole
            // content root avoids that entirely.
            _tempContentRoot = Path.Combine(Path.GetTempPath(), "aalg-trips-tests-" + Guid.NewGuid().ToString("N"));
            SeedContentRoot(_tempContentRoot, Path.Combine(webDir, "wwwroot"));

            var port = GetFreePort();
            BaseUrl = $"http://127.0.0.1:{port}";
            var passwordHash = HashPassword(TestPassword, TestSalt);

            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = webDir,
            };
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--project");
            psi.ArgumentList.Add(webProject);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");

            // Skip launchSettings.json: its profile forces ASPNETCORE_ENVIRONMENT
            // to Development, which turns on static web assets — those serve static
            // files (including albums/markers.json) from the source project wwwroot
            // via the build manifest, overriding the seeded web root. Running in
            // Production keeps static serving pointed at the isolated web root.
            psi.ArgumentList.Add("--no-launch-profile");
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add($"--contentRoot={_tempContentRoot}");
            psi.ArgumentList.Add($"--urls={BaseUrl}");
            psi.ArgumentList.Add("--forcessl=false");
            psi.ArgumentList.Add($"--user:username={TestUsername}");
            psi.ArgumentList.Add($"--user:salt={TestSalt}");
            psi.ArgumentList.Add($"--user:password={passwordHash}");

            // A viewer-role account under the Users section.
            psi.ArgumentList.Add($"--Users:{TestViewerUsername}:salt={TestSalt}");
            psi.ArgumentList.Add($"--Users:{TestViewerUsername}:password={HashPassword(TestViewerPassword, TestSalt)}");
            psi.ArgumentList.Add($"--Users:{TestViewerUsername}:role=viewer");

            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

            _app = Process.Start(psi)!;

            // Drain stdout/stderr so a full pipe never blocks the app.
            _app.OutputDataReceived += (_, _) => { };
            _app.ErrorDataReceived += (_, _) => { };
            _app.BeginOutputReadLine();
            _app.BeginErrorReadLine();

            await WaitForServer(BaseUrl, TimeSpan.FromSeconds(90));
        }

        [OneTimeTearDown]
        public void StopServer()
        {
            try
            {
                if (_app is { HasExited: false })
                {
                    _app.Kill(entireProcessTree: true);
                }
            }
            catch
            { /* best effort */
            }

            _app?.Dispose();

            try
            {
                if (_tempContentRoot is not null && Directory.Exists(_tempContentRoot))
                {
                    Directory.Delete(_tempContentRoot, recursive: true);
                }
            }
            catch
            { /* best effort */
            }
        }

        private static void SeedContentRoot(string contentRoot, string sourceWebRoot)
        {
            // Copy the app's real static assets (css/js/img/fonts and the
            // LibMan-restored lib/) into the temp web root so the pages under test
            // load their actual front-end. Album content is NOT a static file any
            // more: it lives under App_Data (outside the web root) and is served
            // only through the authenticated media endpoint, so it is seeded there
            // instead. The real wwwroot's own albums/ is skipped defensively.
            string webRoot = Path.Combine(contentRoot, "wwwroot");
            if (Directory.Exists(sourceWebRoot))
            {
                CopyDirectory(sourceWebRoot, webRoot, excludeTopLevelDir: "albums");
            }

            var albums = Path.Combine(contentRoot, "App_Data", "albums");
            var album = Path.Combine(albums, SampleAlbumSlug);
            Directory.CreateDirectory(Path.Combine(album, "thumbnail"));

            var meta = new
            {
                DisplayName = SampleAlbumTitle,
                Description = "A sample album used by the baseline Playwright tests.",
                Visited = "2026-01-01T00:00:00",
                Latitude = 55.953251,
                Longitude = -3.188267,
            };
            File.WriteAllText(Path.Combine(album, "data.json"), JsonSerializer.Serialize(meta));

            // Seed markers.json with a real marker plus a stale one for an album that
            // does not exist, so the app's on-startup marker rebuild is exercised: it
            // must drop the ghost (far from the real album, so it would otherwise show
            // as a second, separate pin) and keep only the seeded album.
            var markers = new[]
            {
                new { Lat = 55.953251, Long = -3.188267, Slug = SampleAlbumSlug, Name = SampleAlbumTitle, Date = "Jan 2026", Photos = 0 },
                new { Lat = 51.5, Long = -0.12, Slug = "removed-album", Name = "Removed Album", Date = "Jan 2020", Photos = 3 },
            };
            File.WriteAllText(Path.Combine(albums, "markers.json"), JsonSerializer.Serialize(markers));

            // Seed a sample cruise so the read-only cruise tests (detail render,
            // actions menu) have stable content. Cruise content lives under a
            // "cruises" prefix within the albums root; cruises.json is rebuilt from
            // it on startup, so only the per-cruise cruise.json needs seeding. The
            // itinerary mixes a port that links the sample album, a day at sea (no
            // coordinates), and a second port, exercising the whole shape.
            var cruiseDir = Path.Combine(albums, "cruises", SampleCruiseSlug);
            Directory.CreateDirectory(cruiseDir);

            var cruise = new
            {
                DisplayName = SampleCruiseTitle,
                Description = "A sample cruise used by the baseline Playwright tests.",
                StartDate = "2025-07-15T00:00:00",
                EndDate = "2025-07-22T00:00:00",
                People = new[] { "Gary" },
                Stops = new object[]
                {
                    new
                    {
                        Key = "rome",
                        Date = "2025-07-15T00:00:00",
                        Name = "Rome",
                        AtSea = false,
                        Arrive = (string?)null,
                        Depart = "18:00",
                        Latitude = (double?)41.9028,
                        Longitude = (double?)12.4964,
                        Trips = new[] { SampleAlbumSlug },
                    },
                    new
                    {
                        Key = "cruising",
                        Date = "2025-07-16T00:00:00",
                        Name = "Cruising",
                        AtSea = true,
                        Arrive = (string?)null,
                        Depart = (string?)null,
                        Latitude = (double?)null,
                        Longitude = (double?)null,
                        Trips = Array.Empty<string>(),
                    },
                    new
                    {
                        Key = "santorini",
                        Date = "2025-07-17T00:00:00",
                        Name = "Santorini",
                        AtSea = false,
                        Arrive = "08:00",
                        Depart = "17:00",
                        Latitude = (double?)36.3932,
                        Longitude = (double?)25.4615,
                        Trips = Array.Empty<string>(),
                    },
                },
            };
            File.WriteAllText(Path.Combine(cruiseDir, "cruise.json"), JsonSerializer.Serialize(cruise));
        }

        // Mirrors the app's AalgTrips.Models.PasswordHasher: PBKDF2/HMAC-SHA256,
        // 600,000 iterations, 256-bit key, salt as UTF-8 bytes, upper-case hex.
        // This project is black-box and cannot reference the app assembly, so the
        // derivation is duplicated here — keep it in sync with PasswordHasher.
        private static string HashPassword(string password, string salt)
        {
            var saltBytes = Encoding.UTF8.GetBytes(salt);
            var hash = Rfc2898DeriveBytes.Pbkdf2(password, saltBytes, 600_000, HashAlgorithmName.SHA256, 256 / 8);
            return Convert.ToHexString(hash);
        }

        private static void CopyDirectory(string source, string destination, string? excludeTopLevelDir = null)
        {
            var excludePrefix = excludeTopLevelDir is null
                ? null
                : excludeTopLevelDir + Path.DirectorySeparatorChar;

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);

                if (excludePrefix is not null && relative.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string FindWebProject()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "src", "AalgTrips", "AalgTrips.csproj");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }

            throw new FileNotFoundException("Could not locate src/AalgTrips/AalgTrips.csproj above the test output directory.");
        }

        private static async Task WaitForServer(string baseUrl, TimeSpan timeout)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var deadline = DateTime.UtcNow + timeout;
            Exception? last = null;

            while (DateTime.UtcNow < deadline)
            {
                if (_app is { HasExited: true })
                {
                    throw new InvalidOperationException($"The web app exited early with code {_app.ExitCode}.");
                }

                try
                {
                    var res = await client.GetAsync(baseUrl + "/");
                    if ((int)res.StatusCode < 500)
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }

                await Task.Delay(500);
            }

            throw new TimeoutException($"Web app did not become ready at {baseUrl} within {timeout.TotalSeconds:n0}s.", last);
        }
    }
}