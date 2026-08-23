using System.Text.Json;
using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Shared scaffolding for the unit tests that drive the in-memory catalogues
    /// (<see cref="AlbumCollection"/> and <see cref="JourneyCollection"/>) over a
    /// <see cref="LocalDiskPhotoStore"/>: a fresh temp albums root per test, a store
    /// pointed at it, an album seeder, and a reader for the generated root JSON
    /// files. Content-type-specific seeding (e.g. journeys) stays in the fixture that
    /// needs it.
    /// </summary>
    public abstract class LocalStoreTestBase
    {
        /// <summary>Gets the temp root directory created for the current test.</summary>
        protected string Root { get; private set; } = string.Empty;

        /// <summary>Gets the albums root the store is pointed at (its root key space).</summary>
        protected string AlbumsRoot { get; private set; } = string.Empty;

        [SetUp]
        public void CreateRoot()
        {
            Root = Path.Combine(Path.GetTempPath(), "aalg-trips-unit-" + Guid.NewGuid().ToString("N"));
            AlbumsRoot = Path.Combine(Root, "albums");
            Directory.CreateDirectory(AlbumsRoot);
        }

        [TearDown]
        public void DeleteRoot()
        {
            try
            {
                if (Directory.Exists(Root))
                {
                    Directory.Delete(Root, recursive: true);
                }
            }
            catch
            { /* best effort */
            }
        }

        /// <summary>
        /// Creates a local-disk store over the current test's albums root.
        /// </summary>
        /// <returns>The store under test.</returns>
        protected LocalDiskPhotoStore Store()
        {
            return new LocalDiskPhotoStore(AlbumsRoot);
        }

        /// <summary>
        /// Seeds an album folder (its <c>data.json</c> and, optionally, some empty
        /// photo files) directly on disk, the way the store would hold it.
        /// </summary>
        /// <param name="slug">The album id / folder name.</param>
        /// <param name="displayName">The album's display name.</param>
        /// <param name="photoCount">How many empty photo files to create.</param>
        /// <param name="latitude">The album's latitude.</param>
        /// <param name="longitude">The album's longitude.</param>
        /// <param name="visited">The album's visited date; defaults to 1 Jan 2026.</param>
        /// <returns>The album folder path.</returns>
        protected string SeedAlbumOnDisk(string slug, string displayName, int photoCount = 0, double latitude = 0, double longitude = 0, DateTime? visited = null)
        {
            var path = Path.Combine(AlbumsRoot, slug);
            Directory.CreateDirectory(path);

            var meta = new AlbumMetaData
            {
                DisplayName = displayName,
                Description = displayName + " description",
                Visited = visited ?? new DateTime(2026, 1, 1),
                Latitude = latitude,
                Longitude = longitude,
            };
            File.WriteAllText(Path.Combine(path, "data.json"), JsonSerializer.Serialize(meta));

            for (int i = 0; i < photoCount; i++)
            {
                File.WriteAllText(Path.Combine(path, $"photo-{i}.jpg"), string.Empty);
            }

            return path;
        }

        /// <summary>
        /// Reads and deserializes one of the generated root JSON files the store
        /// writes (for example <c>markers.json</c> or <c>journeys.json</c>).
        /// </summary>
        /// <typeparam name="T">The type to deserialize the file into.</typeparam>
        /// <param name="fileName">The root file name.</param>
        /// <returns>The deserialized contents.</returns>
        protected T ReadRootJson<T>(string fileName)
        {
            var json = File.ReadAllText(Path.Combine(AlbumsRoot, fileName));
            return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }
}