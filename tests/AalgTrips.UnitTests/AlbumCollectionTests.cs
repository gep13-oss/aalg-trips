using System.Collections.Concurrent;
using System.Text.Json;
using AalgTrips.Models;

namespace AalgTrips.UnitTests
{
    /// <summary>
    /// Direct coverage for <see cref="AlbumCollection"/> and its copy-on-write
    /// mutation methods, exercised over a <see cref="LocalDiskPhotoStore"/> pointed
    /// at a temp albums root. The singleton collection is read by public requests
    /// while admin handlers mutate it, so these tests pin down the two properties
    /// that matter: mutations are visible and correct, and concurrent reads never
    /// see a half-applied change. They run without a server; the UITests suite
    /// proves the same behaviour end-to-end over HTTP.
    /// </summary>
    [TestFixture]
    public class AlbumCollectionTests : LocalStoreTestBase
    {
        [Test]
        public void Add_inserts_the_album_and_orders_newest_visited_first()
        {
            var ac = new AlbumCollection(Store());

            ac.Add(new Album("apple", ac, Meta("Apple", new DateTime(2020, 5, 1))));
            ac.Add(new Album("zebra", ac, Meta("Zebra", new DateTime(2026, 5, 1))));

            // Newest trip first, regardless of insertion order or name.
            Assert.That(ac.Albums.Select(a => a.Id), Is.EqualTo(new[] { "zebra", "apple" }));
        }

        [Test]
        public void Albums_are_ordered_newest_visited_first_then_by_id()
        {
            SeedAlbumOnDisk("older", "Older", photoCount: 0, visited: new DateTime(2021, 1, 1));
            SeedAlbumOnDisk("newer", "Newer", photoCount: 0, visited: new DateTime(2025, 1, 1));
            SeedAlbumOnDisk("same-b", "Same B", photoCount: 0, visited: new DateTime(2023, 6, 1));
            SeedAlbumOnDisk("same-a", "Same A", photoCount: 0, visited: new DateTime(2023, 6, 1));
            var ac = new AlbumCollection(Store());

            Assert.That(
                ac.Albums.Select(a => a.Id),
                Is.EqualTo(new[] { "newer", "same-a", "same-b", "older" }),
                "newest visited first; albums sharing a date fall back to id order");
        }

        [Test]
        public void Remove_takes_the_matching_album_out_and_leaves_the_rest()
        {
            var ac = new AlbumCollection(Store());
            ac.Add(new Album("apple", ac, Meta("Apple")));
            ac.Add(new Album("banana", ac, Meta("Banana")));

            ac.Remove("APPLE");

            Assert.That(ac.Albums.Select(a => a.Id), Is.EqualTo(new[] { "banana" }));
        }

        [Test]
        public void ReloadAlbum_refreshes_metadata_and_keeps_photos()
        {
            SeedAlbumOnDisk("trip", "Trip", photoCount: 1);
            var ac = new AlbumCollection(Store());

            var before = ac.Albums.Single(a => a.Id == "trip");
            Assert.That(before.Photos, Has.Count.EqualTo(1), "precondition: the seeded album has a photo");

            // Rewrite the album's data.json the way an edit would, then reload it.
            File.WriteAllText(Path.Combine(AlbumsRoot, "trip", "data.json"), JsonSerializer.Serialize(Meta("Trip Renamed")));
            ac.ReloadAlbum("trip");

            var after = ac.Albums.Single(a => a.Id == "trip");
            Assert.Multiple(() =>
            {
                Assert.That(after.DisplayName, Is.EqualTo("Trip Renamed"), "the refreshed metadata should be visible");
                Assert.That(after.Photos, Has.Count.EqualTo(1), "reloading must not drop the album's photos");
                Assert.That(after.Id, Is.EqualTo("trip"), "the album must keep its id");
            });
        }

        [Test]
        public async Task WriteMarkersAsync_writes_one_marker_per_album_from_the_current_set()
        {
            SeedAlbumOnDisk("edinburgh", "Edinburgh", photoCount: 0, latitude: 55.95, longitude: -3.19);
            SeedAlbumOnDisk("paris", "Paris", photoCount: 2, latitude: 48.85, longitude: 2.35, visited: new DateTime(2026, 1, 15));
            var ac = new AlbumCollection(Store());

            await ac.WriteMarkersAsync();

            var markers = ReadRootJson<List<Marker>>(PhotoStoreConventions.MarkersFileName);
            Assert.That(markers.Select(m => m.Slug), Is.EquivalentTo(new[] { "edinburgh", "paris" }));

            var paris = markers.Single(m => m.Slug == "paris");
            Assert.Multiple(() =>
            {
                Assert.That(paris.Lat, Is.EqualTo(48.85));
                Assert.That(paris.Long, Is.EqualTo(2.35));

                // The tooltip fields: display name, invariant "d MMM yyyy" date (with the
                // day, to match the trip card, the album page and the port pin), and the
                // album's photo count travel with each marker.
                Assert.That(paris.Name, Is.EqualTo("Paris"));
                Assert.That(paris.Date, Is.EqualTo("15 Jan 2026"));
                Assert.That(paris.Photos, Is.EqualTo(2));
            });
        }

        [Test]
        public void Concurrent_mutations_and_reads_never_throw_and_leave_a_consistent_set()
        {
            SeedAlbumOnDisk("a", "A", photoCount: 0);
            SeedAlbumOnDisk("b", "B", photoCount: 0);
            SeedAlbumOnDisk("c", "C", photoCount: 0);
            var ac = new AlbumCollection(Store());

            var errors = new ConcurrentQueue<Exception>();
            var stop = new bool[1];

            // Readers enumerate and index the collection the way Razor and the
            // Next/Previous paginator do. Before the copy-on-write refactor an
            // in-place Insert/Remove during this enumeration threw "collection was
            // modified".
            var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                try
                {
                    while (!Volatile.Read(ref stop[0]))
                    {
                        foreach (var album in ac.Albums)
                        {
                            _ = ac.Albums.IndexOf(album);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })).ToArray();

            var writers = Enumerable.Range(0, 60).Select(i => Task.Run(() =>
            {
                try
                {
                    var slug = "t" + i;
                    SeedAlbumOnDisk(slug, slug.ToUpperInvariant(), photoCount: 0);
                    ac.Add(new Album(slug, ac, Meta(slug)));
                    ac.ReloadAlbum(slug);
                    ac.Remove(slug);
                }
                catch (Exception ex)
                {
                    errors.Enqueue(ex);
                }
            })).ToArray();

            Task.WaitAll(writers);
            Volatile.Write(ref stop[0], true);
            Task.WaitAll(readers);

            Assert.That(errors, Is.Empty, "no reader or writer should have thrown");
            Assert.That(ac.Albums.Select(a => a.Id), Is.EquivalentTo(new[] { "a", "b", "c" }), "only the original albums should remain");
        }

        private static AlbumMetaData Meta(string displayName, DateTime? visited = null)
        {
            return new AlbumMetaData
            {
                DisplayName = displayName,
                Description = displayName + " description",
                Visited = visited ?? new DateTime(2026, 1, 1),
            };
        }
    }
}