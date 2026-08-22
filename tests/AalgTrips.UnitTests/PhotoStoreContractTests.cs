using System.Text;
using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// The behaviour every <see cref="IPhotoStore"/> implementation must satisfy,
    /// run against each concrete store so the local disk store and the Azure Blob
    /// store are proven to behave identically. A derived fixture supplies a fresh,
    /// isolated store via <see cref="CreateStore"/>.
    /// </summary>
    public abstract class PhotoStoreContractTests
    {
        private const string Album = "sample-trip";
        private const string Cruise = "sample-cruise";
        private const string Stop = "rome";

        /// <summary>
        /// Creates a fresh, isolated store for a single test.
        /// </summary>
        /// <returns>The store under test.</returns>
        protected abstract IPhotoStore CreateStore();

        [Test]
        public async Task Saved_photo_is_listed_readable_and_has_a_url()
        {
            var store = CreateStore();
            var bytes = Encoding.UTF8.GetBytes("original-photo-bytes");

            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(bytes));

            Assert.Multiple(() =>
            {
                Assert.That(store.PhotoExists(Album, "beach.jpg"), Is.True);
                Assert.That(store.ListPhotoFileNames(Album), Does.Contain("beach.jpg"));
                Assert.That(ReadAll(store.OpenPhoto(Album, "beach.jpg")), Is.EqualTo(bytes));
                Assert.That(store.PhotoUrl(Album, "beach.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Metadata_round_trips_and_the_album_is_listed()
        {
            var store = CreateStore();
            var metadata = new AlbumMetaData
            {
                DisplayName = "Sample Trip",
                Description = "A sample",
                Latitude = 55.95,
                Longitude = -3.19,
            };

            await store.WriteMetadataAsync(Album, metadata);

            var read = store.TryReadMetadata(Album);
            Assert.Multiple(() =>
            {
                Assert.That(store.AlbumExists(Album), Is.True);
                Assert.That(store.ListAlbumIds(), Does.Contain(Album));
                Assert.That(read, Is.Not.Null);
                Assert.That(read.DisplayName, Is.EqualTo("Sample Trip"));
                Assert.That(read.Latitude, Is.EqualTo(55.95));
                Assert.That(read.Longitude, Is.EqualTo(-3.19));
            });
        }

        [Test]
        public void Missing_metadata_reads_as_null()
        {
            var store = CreateStore();

            Assert.That(store.TryReadMetadata("no-such-album"), Is.Null);
        }

        [Test]
        public async Task Thumbnail_is_listed_and_has_a_url()
        {
            var store = CreateStore();

            await store.SaveThumbnailAsync(Album, "beach-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("thumb")));

            Assert.Multiple(() =>
            {
                Assert.That(store.ListThumbnailFileNames(Album), Does.Contain("beach-190x127.jpg"));
                Assert.That(store.ThumbnailUrl(Album, "beach-190x127.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Deleting_a_photo_removes_it_and_only_its_own_thumbnails()
        {
            var store = CreateStore();
            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveThumbnailAsync(Album, "beach-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t1")));
            await store.SaveThumbnailAsync(Album, "sunset-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t2")));

            await store.DeletePhotoAsync(Album, "beach.jpg");

            Assert.Multiple(() =>
            {
                Assert.That(store.PhotoExists(Album, "beach.jpg"), Is.False);
                Assert.That(store.ListThumbnailFileNames(Album), Does.Not.Contain("beach-190x127.jpg"), "the photo's own thumbnail should go");
                Assert.That(store.ListThumbnailFileNames(Album), Does.Contain("sunset-190x127.jpg"), "another photo's thumbnail must be left alone");
            });
        }

        [Test]
        public async Task Renaming_a_photo_moves_it_and_its_thumbnails()
        {
            var store = CreateStore();
            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveThumbnailAsync(Album, "beach-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t")));

            await store.RenamePhotoAsync(Album, "beach.jpg", "shore.jpg");

            Assert.Multiple(() =>
            {
                Assert.That(store.PhotoExists(Album, "shore.jpg"), Is.True);
                Assert.That(store.PhotoExists(Album, "beach.jpg"), Is.False);
                Assert.That(store.ListThumbnailFileNames(Album), Does.Contain("shore-190x127.jpg"));
                Assert.That(store.ListThumbnailFileNames(Album), Does.Not.Contain("beach-190x127.jpg"));
            });
        }

        [Test]
        public async Task Renaming_an_album_moves_all_of_its_content_to_the_new_id()
        {
            var store = CreateStore();
            await store.WriteMetadataAsync(Album, new AlbumMetaData { DisplayName = "Sample" });
            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveThumbnailAsync(Album, "beach-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t")));

            await store.RenameAlbumAsync(Album, "renamed-trip");

            Assert.Multiple(() =>
            {
                Assert.That(store.AlbumExists("renamed-trip"), Is.True);
                Assert.That(store.ListPhotoFileNames("renamed-trip"), Does.Contain("beach.jpg"));
                Assert.That(store.ListThumbnailFileNames("renamed-trip"), Does.Contain("beach-190x127.jpg"));
                Assert.That(store.TryReadMetadata("renamed-trip")?.DisplayName, Is.EqualTo("Sample"), "metadata moves with the album");

                Assert.That(store.AlbumExists(Album), Is.False, "nothing should be left under the old id");
                Assert.That(store.ListPhotoFileNames(Album), Is.Empty);
                Assert.That(store.ListThumbnailFileNames(Album), Is.Empty);
                Assert.That(store.TryReadMetadata(Album), Is.Null);
            });
        }

        [Test]
        public async Task Deleting_an_album_removes_all_of_its_content()
        {
            var store = CreateStore();
            await store.WriteMetadataAsync(Album, new AlbumMetaData { DisplayName = "Sample" });
            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveThumbnailAsync(Album, "beach-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t")));

            await store.DeleteAlbumAsync(Album);

            Assert.Multiple(() =>
            {
                Assert.That(store.AlbumExists(Album), Is.False);
                Assert.That(store.ListPhotoFileNames(Album), Is.Empty);
                Assert.That(store.ListThumbnailFileNames(Album), Is.Empty);
                Assert.That(store.TryReadMetadata(Album), Is.Null);
            });
        }

        [Test]
        public async Task Content_opens_by_key_and_missing_content_reports_false()
        {
            var store = CreateStore();
            var bytes = Encoding.UTF8.GetBytes("photo-bytes");
            await store.SavePhotoAsync(Album, "beach.jpg", new MemoryStream(bytes));

            bool opened = store.TryOpenContent($"{Album}/beach.jpg", out var content);

            Assert.Multiple(() =>
            {
                Assert.That(opened, Is.True);
                Assert.That(ReadAll(content), Is.EqualTo(bytes));
                Assert.That(store.TryOpenContent($"{Album}/missing.jpg", out _), Is.False);
            });
        }

        [Test]
        public async Task Writing_markers_succeeds_and_the_marker_url_is_set()
        {
            var store = CreateStore();

            await store.WriteMarkersAsync(new[] { new Marker { Lat = 55.95, Long = -3.19, Slug = Album } });

            Assert.That(store.MarkersUrl(), Is.Not.Empty);
        }

        [Test]
        public async Task Cruise_metadata_round_trips_and_the_cruise_is_listed()
        {
            var store = CreateStore();
            var metadata = new CruiseMetaData
            {
                DisplayName = "Mediterranean Cruise",
                Description = "Round the Med",
                StartDate = new DateTime(2025, 7, 27),
                EndDate = new DateTime(2025, 8, 3),
                People = new List<string> { "Gary", "Lynn" },
                Stops = new List<CruiseStop>
                {
                    new CruiseStop { Date = new DateTime(2025, 7, 27), Name = "Rome", Depart = "17:00", Latitude = 42.09, Longitude = 11.80, Trips = new List<string> { "colosseum" } },
                    new CruiseStop { Date = new DateTime(2025, 7, 28), Name = "Cruising", AtSea = true },
                },
            };

            await store.WriteCruiseAsync(Cruise, metadata);

            var read = store.TryReadCruise(Cruise);
            Assert.Multiple(() =>
            {
                Assert.That(store.CruiseExists(Cruise), Is.True);
                Assert.That(store.ListCruiseIds(), Does.Contain(Cruise));
                Assert.That(read, Is.Not.Null);
                Assert.That(read.DisplayName, Is.EqualTo("Mediterranean Cruise"));
                Assert.That(read.People, Is.EqualTo(new[] { "Gary", "Lynn" }));
                Assert.That(read.Stops, Has.Count.EqualTo(2));

                // The port day keeps its coordinates and linked trips; the day at
                // sea round-trips with no coordinates.
                Assert.That(read.Stops[0].Latitude, Is.EqualTo(42.09));
                Assert.That(read.Stops[0].Trips, Is.EqualTo(new[] { "colosseum" }));
                Assert.That(read.Stops[1].AtSea, Is.True);
                Assert.That(read.Stops[1].Latitude, Is.Null);
            });
        }

        [Test]
        public void Missing_cruise_reads_as_null()
        {
            var store = CreateStore();

            Assert.That(store.TryReadCruise("no-such-cruise"), Is.Null);
        }

        [Test]
        public async Task A_cruise_is_not_listed_as_an_album()
        {
            var store = CreateStore();

            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });

            Assert.Multiple(() =>
            {
                Assert.That(store.CruiseExists(Cruise), Is.True);
                Assert.That(store.ListAlbumIds(), Does.Not.Contain(PhotoStoreConventions.CruisesFolder), "the cruises area must not surface as an album");
                Assert.That(store.ListAlbumIds(), Does.Not.Contain(Cruise));
                Assert.That(store.AlbumExists(Cruise), Is.False);
            });
        }

        [Test]
        public async Task Renaming_a_cruise_moves_its_content_to_the_new_id()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });

            await store.RenameCruiseAsync(Cruise, "renamed-cruise");

            Assert.Multiple(() =>
            {
                Assert.That(store.CruiseExists("renamed-cruise"), Is.True);
                Assert.That(store.TryReadCruise("renamed-cruise")?.DisplayName, Is.EqualTo("Sample"), "metadata moves with the cruise");
                Assert.That(store.CruiseExists(Cruise), Is.False, "nothing should be left under the old id");
                Assert.That(store.TryReadCruise(Cruise), Is.Null);
            });
        }

        [Test]
        public async Task Deleting_a_cruise_removes_it()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });

            await store.DeleteCruiseAsync(Cruise);

            Assert.Multiple(() =>
            {
                Assert.That(store.CruiseExists(Cruise), Is.False);
                Assert.That(store.TryReadCruise(Cruise), Is.Null);
                Assert.That(store.ListCruiseIds(), Does.Not.Contain(Cruise));
            });
        }

        [Test]
        public async Task Writing_cruises_succeeds_and_the_cruise_url_is_set()
        {
            var store = CreateStore();

            await store.WriteCruisesAsync(new[]
            {
                new CruiseRoute { Slug = Cruise, Name = "Sample", Ports = new List<CruisePort>() },
            });

            Assert.That(store.CruisesUrl(), Is.Not.Empty);
        }

        [Test]
        public async Task Cruise_route_round_trips_and_a_missing_route_reads_as_null()
        {
            var store = CreateStore();

            Assert.That(store.TryReadCruiseRoute(Cruise), Is.Null, "a cruise with no uploaded route reads as null");

            await store.SaveCruiseRouteAsync(Cruise, new[]
            {
                new[] { 41.90, 12.50 },
                new[] { 40.85, 14.27 },
            });

            var read = store.TryReadCruiseRoute(Cruise);

            Assert.Multiple(() =>
            {
                Assert.That(read, Is.Not.Null);
                Assert.That(read, Has.Count.EqualTo(2));
                Assert.That(read[0], Is.EqualTo(new[] { 41.90, 12.50 }).Within(1e-9));
                Assert.That(read[1], Is.EqualTo(new[] { 40.85, 14.27 }).Within(1e-9));
            });
        }

        [Test]
        public async Task Deleting_a_cruise_route_leaves_the_cruise_itself()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });
            await store.SaveCruiseRouteAsync(Cruise, new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } });

            await store.DeleteCruiseRouteAsync(Cruise);

            Assert.Multiple(() =>
            {
                Assert.That(store.TryReadCruiseRoute(Cruise), Is.Null);
                Assert.That(store.CruiseExists(Cruise), Is.True, "only the route is removed");
            });
        }

        [Test]
        public async Task A_cruise_route_moves_on_rename_and_is_removed_with_the_cruise()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });
            await store.SaveCruiseRouteAsync(Cruise, new[] { new[] { 1.0, 2.0 }, new[] { 3.0, 4.0 } });

            await store.RenameCruiseAsync(Cruise, "renamed-cruise");

            Assert.Multiple(() =>
            {
                Assert.That(store.TryReadCruiseRoute("renamed-cruise"), Has.Count.EqualTo(2), "the route moves with the cruise");
                Assert.That(store.TryReadCruiseRoute(Cruise), Is.Null, "nothing is left under the old id");
            });

            await store.DeleteCruiseAsync("renamed-cruise");

            Assert.That(store.TryReadCruiseRoute("renamed-cruise"), Is.Null, "the route is removed with the cruise");
        }

        [Test]
        public async Task Cruise_stop_photo_is_saved_listed_readable_and_has_a_url()
        {
            var store = CreateStore();
            var bytes = Encoding.UTF8.GetBytes("cruise-photo-bytes");

            await store.SaveCruisePhotoAsync(Cruise, Stop, "deck.jpg", new MemoryStream(bytes));

            Assert.Multiple(() =>
            {
                Assert.That(store.CruisePhotoExists(Cruise, Stop, "deck.jpg"), Is.True);
                Assert.That(store.ListCruisePhotoFileNames(Cruise, Stop), Does.Contain("deck.jpg"));
                Assert.That(ReadAll(store.OpenCruisePhoto(Cruise, Stop, "deck.jpg")), Is.EqualTo(bytes));
                Assert.That(store.CruisePhotoUrl(Cruise, Stop, "deck.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Cruise_stop_thumbnail_is_listed_and_has_a_url()
        {
            var store = CreateStore();

            await store.SaveCruiseThumbnailAsync(Cruise, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("thumb")));

            Assert.Multiple(() =>
            {
                Assert.That(store.ListCruiseThumbnailFileNames(Cruise, Stop), Does.Contain("deck-190x127.jpg"));
                Assert.That(store.CruiseThumbnailUrl(Cruise, Stop, "deck-190x127.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Cruise_stop_photos_are_scoped_to_their_own_stop()
        {
            var store = CreateStore();

            await store.SaveCruisePhotoAsync(Cruise, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveCruisePhotoAsync(Cruise, "santorini", "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("b")));

            Assert.Multiple(() =>
            {
                Assert.That(store.ListCruisePhotoFileNames(Cruise, Stop), Is.EqualTo(new[] { "deck.jpg" }));
                Assert.That(store.ListCruisePhotoFileNames(Cruise, "santorini"), Is.EqualTo(new[] { "beach.jpg" }));
            });
        }

        [Test]
        public async Task Deleting_a_cruise_stop_photo_removes_it_and_only_its_own_thumbnails()
        {
            var store = CreateStore();
            await store.SaveCruisePhotoAsync(Cruise, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveCruiseThumbnailAsync(Cruise, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t1")));
            await store.SaveCruiseThumbnailAsync(Cruise, Stop, "sunset-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t2")));

            await store.DeleteCruisePhotoAsync(Cruise, Stop, "deck.jpg");

            Assert.Multiple(() =>
            {
                Assert.That(store.CruisePhotoExists(Cruise, Stop, "deck.jpg"), Is.False);
                Assert.That(store.ListCruiseThumbnailFileNames(Cruise, Stop), Does.Not.Contain("deck-190x127.jpg"), "the photo's own thumbnail should go");
                Assert.That(store.ListCruiseThumbnailFileNames(Cruise, Stop), Does.Contain("sunset-190x127.jpg"), "another photo's thumbnail must be left alone");
            });
        }

        [Test]
        public async Task Deleting_a_cruise_removes_its_stop_photos()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });
            await store.SaveCruisePhotoAsync(Cruise, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveCruiseThumbnailAsync(Cruise, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t")));

            await store.DeleteCruiseAsync(Cruise);

            Assert.Multiple(() =>
            {
                Assert.That(store.CruiseExists(Cruise), Is.False);
                Assert.That(store.CruisePhotoExists(Cruise, Stop, "deck.jpg"), Is.False, "a deleted cruise takes its stop photos with it");
                Assert.That(store.ListCruisePhotoFileNames(Cruise, Stop), Is.Empty);
            });
        }

        [Test]
        public async Task Renaming_a_cruise_moves_its_stop_photos_to_the_new_id()
        {
            var store = CreateStore();
            await store.WriteCruiseAsync(Cruise, new CruiseMetaData { DisplayName = "Sample" });
            await store.SaveCruisePhotoAsync(Cruise, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));

            await store.RenameCruiseAsync(Cruise, "renamed-cruise");

            Assert.Multiple(() =>
            {
                Assert.That(store.CruisePhotoExists("renamed-cruise", Stop, "deck.jpg"), Is.True, "stop photos move with the cruise");
                Assert.That(store.CruisePhotoExists(Cruise, Stop, "deck.jpg"), Is.False, "nothing should be left under the old id");
            });
        }

        private static byte[] ReadAll(Stream stream)
        {
            using (stream)
            {
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }
    }
}