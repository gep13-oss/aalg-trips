using System.Text.Json;
using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Direct coverage for <see cref="JourneyCollection"/> over a
    /// <see cref="LocalDiskPhotoStore"/> pointed at a temp root: the copy-on-write
    /// mutations are visible and correctly ordered, a journey reloads from the
    /// store, the generated route drops days at sea and keeps the ports in order,
    /// and journeys never leak into the album catalogue. Runs without a server; the
    /// UITests suite proves the same behaviour end-to-end.
    /// </summary>
    [TestFixture]
    public class JourneyCollectionTests : LocalStoreTestBase
    {
        [Test]
        public void Add_inserts_the_journey_and_orders_newest_departure_first()
        {
            var cc = new JourneyCollection(Store());

            cc.Add(new Journey("apple", Meta("Apple", new DateTime(2020, 5, 1))));
            cc.Add(new Journey("zebra", Meta("Zebra", new DateTime(2026, 5, 1))));

            // Newest departure first, regardless of insertion order or name.
            Assert.That(cc.Journeys.Select(c => c.Id), Is.EqualTo(new[] { "zebra", "apple" }));
        }

        [Test]
        public void Journeys_are_ordered_newest_departure_first_then_by_id()
        {
            SeedJourneyOnDisk("older", "Older", new DateTime(2021, 1, 1));
            SeedJourneyOnDisk("newer", "Newer", new DateTime(2025, 1, 1));
            SeedJourneyOnDisk("same-b", "Same B", new DateTime(2023, 6, 1));
            SeedJourneyOnDisk("same-a", "Same A", new DateTime(2023, 6, 1));
            var cc = new JourneyCollection(Store());

            Assert.That(
                cc.Journeys.Select(c => c.Id),
                Is.EqualTo(new[] { "newer", "same-a", "same-b", "older" }),
                "newest departure first; journeys sharing a start date fall back to id order");
        }

        [Test]
        public void Remove_takes_the_matching_journey_out_and_leaves_the_rest()
        {
            var cc = new JourneyCollection(Store());
            cc.Add(new Journey("apple", Meta("Apple")));
            cc.Add(new Journey("banana", Meta("Banana")));

            cc.Remove("APPLE");

            Assert.That(cc.Journeys.Select(c => c.Id), Is.EqualTo(new[] { "banana" }));
        }

        [Test]
        public void ReloadJourney_refreshes_the_metadata()
        {
            SeedJourneyOnDisk("trip", "Trip", new DateTime(2025, 1, 1));
            var cc = new JourneyCollection(Store());

            File.WriteAllText(
                Path.Combine(AlbumsRoot, "journeys", "trip", "journey.json"),
                JsonSerializer.Serialize(Meta("Trip Renamed")));
            cc.ReloadJourney("trip");

            Assert.That(cc.Journeys.Single(c => c.Id == "trip").DisplayName, Is.EqualTo("Trip Renamed"));
        }

        [Test]
        public async Task WriteJourneysAsync_writes_ports_in_order_and_skips_days_at_sea()
        {
            SeedJourneyOnDisk("med", "Med Journey", new DateTime(2025, 7, 27), new List<JourneyStop>
            {
                new JourneyStop { Date = new DateTime(2025, 7, 27), Name = "Rome", Depart = "17:00", Latitude = 42.09, Longitude = 11.80, Trips = new List<string> { "colosseum", "vatican-city" } },
                new JourneyStop { Date = new DateTime(2025, 7, 28), Name = "Cruising", AtSea = true },
                new JourneyStop { Date = new DateTime(2025, 7, 29), Name = "Santorini", Arrive = "13:00", Depart = "23:00", Latitude = 36.39, Longitude = 25.46 },
            });
            var cc = new JourneyCollection(Store());

            await cc.WriteJourneysAsync();

            var med = ReadRootJson<List<JourneyRoute>>(PhotoStoreConventions.JourneysFileName).Single();
            Assert.Multiple(() =>
            {
                Assert.That(med.Slug, Is.EqualTo("med"));
                Assert.That(med.Name, Is.EqualTo("Med Journey"));

                // The travel day is not a route vertex; the two waypoints are, in order.
                Assert.That(med.Waypoints.Select(p => p.Name), Is.EqualTo(new[] { "Rome", "Santorini" }));
                Assert.That(med.Waypoints[0].Lat, Is.EqualTo(42.09));
                Assert.That(med.Waypoints[0].Date, Is.EqualTo("27 Jul 2025"));
                Assert.That(med.Waypoints[0].Trips, Is.EqualTo(new[] { "colosseum", "vatican-city" }));
                Assert.That(med.Waypoints[1].Arrive, Is.EqualTo("13:00"));
            });
        }

        [Test]
        public void Journeys_are_kept_out_of_the_album_catalogue()
        {
            SeedAlbumOnDisk("edinburgh", "Edinburgh");
            SeedJourneyOnDisk("med", "Med Journey", new DateTime(2025, 7, 27));

            var ac = new AlbumCollection(Store());
            var cc = new JourneyCollection(Store());

            Assert.Multiple(() =>
            {
                Assert.That(ac.Albums.Select(a => a.Id), Is.EqualTo(new[] { "edinburgh" }), "the journey must not appear as an album");
                Assert.That(cc.Journeys.Select(c => c.Id), Is.EqualTo(new[] { "med" }));
            });
        }

        private static JourneyMetaData Meta(string displayName, DateTime? start = null)
        {
            var departed = start ?? new DateTime(2025, 1, 1);
            return new JourneyMetaData
            {
                DisplayName = displayName,
                Description = displayName + " description",
                StartDate = departed,
                EndDate = departed.AddDays(7),
            };
        }

        private void SeedJourneyOnDisk(string slug, string displayName, DateTime? start = null, List<JourneyStop>? stops = null)
        {
            var path = Path.Combine(AlbumsRoot, "journeys", slug);
            Directory.CreateDirectory(path);

            var departed = start ?? new DateTime(2025, 1, 1);
            var meta = new JourneyMetaData
            {
                DisplayName = displayName,
                Description = displayName + " description",
                StartDate = departed,
                EndDate = departed.AddDays(7),
                Stops = stops,
            };
            File.WriteAllText(Path.Combine(path, "journey.json"), JsonSerializer.Serialize(meta));
        }
    }
}