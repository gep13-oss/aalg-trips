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
        private const string Journey = "sample-journey";
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
        public async Task Journey_metadata_round_trips_and_the_journey_is_listed()
        {
            var store = CreateStore();
            var metadata = new JourneyMetaData
            {
                DisplayName = "Mediterranean Journey",
                Description = "Round the Med",
                StartDate = new DateTime(2025, 7, 27),
                EndDate = new DateTime(2025, 8, 3),
                People = new List<string> { "Gary", "Lynn" },
                Stops = new List<JourneyStop>
                {
                    new JourneyStop { Date = new DateTime(2025, 7, 27), Name = "Rome", Depart = "17:00", Latitude = 42.09, Longitude = 11.80, Trips = new List<string> { "colosseum" } },
                    new JourneyStop { Date = new DateTime(2025, 7, 28), Name = "Cruising", AtSea = true },
                },
            };

            await store.WriteJourneyAsync(Journey, metadata);

            var read = store.TryReadJourney(Journey);
            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyExists(Journey), Is.True);
                Assert.That(store.ListJourneyIds(), Does.Contain(Journey));
                Assert.That(read, Is.Not.Null);
                Assert.That(read.DisplayName, Is.EqualTo("Mediterranean Journey"));
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
        public void Missing_journey_reads_as_null()
        {
            var store = CreateStore();

            Assert.That(store.TryReadJourney("no-such-journey"), Is.Null);
        }

        [Test]
        public async Task A_journey_is_not_listed_as_an_album()
        {
            var store = CreateStore();

            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyExists(Journey), Is.True);
                Assert.That(store.ListAlbumIds(), Does.Not.Contain(PhotoStoreConventions.JourneysFolder), "the journeys area must not surface as an album");
                Assert.That(store.ListAlbumIds(), Does.Not.Contain(Journey));
                Assert.That(store.AlbumExists(Journey), Is.False);
            });
        }

        [Test]
        public async Task Renaming_a_journey_moves_its_content_to_the_new_id()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });

            await store.RenameJourneyAsync(Journey, "renamed-journey");

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyExists("renamed-journey"), Is.True);
                Assert.That(store.TryReadJourney("renamed-journey")?.DisplayName, Is.EqualTo("Sample"), "metadata moves with the journey");
                Assert.That(store.JourneyExists(Journey), Is.False, "nothing should be left under the old id");
                Assert.That(store.TryReadJourney(Journey), Is.Null);
            });
        }

        [Test]
        public async Task Deleting_a_journey_removes_it()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });

            await store.DeleteJourneyAsync(Journey);

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyExists(Journey), Is.False);
                Assert.That(store.TryReadJourney(Journey), Is.Null);
                Assert.That(store.ListJourneyIds(), Does.Not.Contain(Journey));
            });
        }

        [Test]
        public async Task Writing_journeys_succeeds_and_the_journey_url_is_set()
        {
            var store = CreateStore();

            await store.WriteJourneysAsync(new[]
            {
                new JourneyRoute { Slug = Journey, Name = "Sample", Waypoints = new List<JourneyWaypoint>() },
            });

            Assert.That(store.JourneysUrl(), Is.Not.Empty);
        }

        [Test]
        public async Task Journey_route_round_trips_and_a_missing_route_reads_as_null()
        {
            var store = CreateStore();

            Assert.That(store.TryReadJourneyRoute(Journey), Is.Null, "a journey with no uploaded route reads as null");

            await store.SaveJourneyRouteAsync(Journey, Segments());

            var read = store.TryReadJourneyRoute(Journey);

            Assert.Multiple(() =>
            {
                Assert.That(read, Is.Not.Null);
                Assert.That(read, Has.Count.EqualTo(2), "a solid track segment and a dashed travel segment");
                Assert.That(read[0].Travel, Is.False);
                Assert.That(read[0].Points, Has.Count.EqualTo(2));
                Assert.That(read[0].Points[0], Is.EqualTo(new[] { 41.90, 12.50 }).Within(1e-9));
                Assert.That(read[1].Travel, Is.True, "the travel hop round-trips as dashed");
                Assert.That(read[1].Points[1], Is.EqualTo(new[] { 34.26, 108.94 }).Within(1e-9));
            });
        }

        [Test]
        public async Task Deleting_a_journey_route_leaves_the_journey_itself()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });
            await store.SaveJourneyRouteAsync(Journey, Segments());

            await store.DeleteJourneyRouteAsync(Journey);

            Assert.Multiple(() =>
            {
                Assert.That(store.TryReadJourneyRoute(Journey), Is.Null);
                Assert.That(store.JourneyExists(Journey), Is.True, "only the route is removed");
            });
        }

        [Test]
        public async Task A_journey_route_moves_on_rename_and_is_removed_with_the_journey()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });
            await store.SaveJourneyRouteAsync(Journey, Segments());

            await store.RenameJourneyAsync(Journey, "renamed-journey");

            Assert.Multiple(() =>
            {
                Assert.That(store.TryReadJourneyRoute("renamed-journey"), Has.Count.EqualTo(2), "the route moves with the journey");
                Assert.That(store.TryReadJourneyRoute(Journey), Is.Null, "nothing is left under the old id");
            });

            await store.DeleteJourneyAsync("renamed-journey");

            Assert.That(store.TryReadJourneyRoute("renamed-journey"), Is.Null, "the route is removed with the journey");
        }

        // A two-segment route: a solid covered track and a dashed travel hop.
        private static RouteSegment[] Segments()
        {
            return new[]
            {
                new RouteSegment { Points = new List<double[]> { new[] { 41.90, 12.50 }, new[] { 40.85, 14.27 } }, Travel = false },
                new RouteSegment { Points = new List<double[]> { new[] { 40.85, 14.27 }, new[] { 34.26, 108.94 } }, Travel = true },
            };
        }

        [Test]
        public async Task Journey_stop_photo_is_saved_listed_readable_and_has_a_url()
        {
            var store = CreateStore();
            var bytes = Encoding.UTF8.GetBytes("journey-photo-bytes");

            await store.SaveJourneyPhotoAsync(Journey, Stop, "deck.jpg", new MemoryStream(bytes));

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyPhotoExists(Journey, Stop, "deck.jpg"), Is.True);
                Assert.That(store.ListJourneyPhotoFileNames(Journey, Stop), Does.Contain("deck.jpg"));
                Assert.That(ReadAll(store.OpenJourneyPhoto(Journey, Stop, "deck.jpg")), Is.EqualTo(bytes));
                Assert.That(store.JourneyPhotoUrl(Journey, Stop, "deck.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Journey_stop_thumbnail_is_listed_and_has_a_url()
        {
            var store = CreateStore();

            await store.SaveJourneyThumbnailAsync(Journey, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("thumb")));

            Assert.Multiple(() =>
            {
                Assert.That(store.ListJourneyThumbnailFileNames(Journey, Stop), Does.Contain("deck-190x127.jpg"));
                Assert.That(store.JourneyThumbnailUrl(Journey, Stop, "deck-190x127.jpg"), Is.Not.Empty);
            });
        }

        [Test]
        public async Task Journey_stop_photos_are_scoped_to_their_own_stop()
        {
            var store = CreateStore();

            await store.SaveJourneyPhotoAsync(Journey, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveJourneyPhotoAsync(Journey, "santorini", "beach.jpg", new MemoryStream(Encoding.UTF8.GetBytes("b")));

            Assert.Multiple(() =>
            {
                Assert.That(store.ListJourneyPhotoFileNames(Journey, Stop), Is.EqualTo(new[] { "deck.jpg" }));
                Assert.That(store.ListJourneyPhotoFileNames(Journey, "santorini"), Is.EqualTo(new[] { "beach.jpg" }));
            });
        }

        [Test]
        public async Task Deleting_a_journey_stop_photo_removes_it_and_only_its_own_thumbnails()
        {
            var store = CreateStore();
            await store.SaveJourneyPhotoAsync(Journey, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveJourneyThumbnailAsync(Journey, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t1")));
            await store.SaveJourneyThumbnailAsync(Journey, Stop, "sunset-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t2")));

            await store.DeleteJourneyPhotoAsync(Journey, Stop, "deck.jpg");

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyPhotoExists(Journey, Stop, "deck.jpg"), Is.False);
                Assert.That(store.ListJourneyThumbnailFileNames(Journey, Stop), Does.Not.Contain("deck-190x127.jpg"), "the photo's own thumbnail should go");
                Assert.That(store.ListJourneyThumbnailFileNames(Journey, Stop), Does.Contain("sunset-190x127.jpg"), "another photo's thumbnail must be left alone");
            });
        }

        [Test]
        public async Task Deleting_a_journey_removes_its_stop_photos()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });
            await store.SaveJourneyPhotoAsync(Journey, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));
            await store.SaveJourneyThumbnailAsync(Journey, Stop, "deck-190x127.jpg", new MemoryStream(Encoding.UTF8.GetBytes("t")));

            await store.DeleteJourneyAsync(Journey);

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyExists(Journey), Is.False);
                Assert.That(store.JourneyPhotoExists(Journey, Stop, "deck.jpg"), Is.False, "a deleted journey takes its stop photos with it");
                Assert.That(store.ListJourneyPhotoFileNames(Journey, Stop), Is.Empty);
            });
        }

        [Test]
        public async Task Renaming_a_journey_moves_its_stop_photos_to_the_new_id()
        {
            var store = CreateStore();
            await store.WriteJourneyAsync(Journey, new JourneyMetaData { DisplayName = "Sample" });
            await store.SaveJourneyPhotoAsync(Journey, Stop, "deck.jpg", new MemoryStream(Encoding.UTF8.GetBytes("a")));

            await store.RenameJourneyAsync(Journey, "renamed-journey");

            Assert.Multiple(() =>
            {
                Assert.That(store.JourneyPhotoExists("renamed-journey", Stop, "deck.jpg"), Is.True, "stop photos move with the journey");
                Assert.That(store.JourneyPhotoExists(Journey, Stop, "deck.jpg"), Is.False, "nothing should be left under the old id");
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