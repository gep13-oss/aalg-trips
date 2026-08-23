using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// The in-memory catalogue of albums. It is registered as a singleton and is
    /// therefore shared across every request: public GET requests enumerate it
    /// while admin handlers add, remove and reload entries. To keep that safe
    /// without forcing every reader to lock, all mutation goes through the methods
    /// below under <see cref="_sync"/>, and each one builds a brand-new list and
    /// swaps the <see cref="Albums"/> reference (copy-on-write). A reader only ever
    /// sees a fully-published list that is never mutated in place, so it cannot
    /// throw a "collection was modified" error or observe a half-applied change.
    /// The album content itself is read from and written to an
    /// <see cref="IPhotoStore"/>, so the catalogue is independent of where photos
    /// physically live (local disk in development, Azure Blob in production).
    /// </summary>
    public class AlbumCollection
    {
        private readonly IPhotoStore _store;
        private readonly object _sync = new object();

        public AlbumCollection(IPhotoStore store)
        {
            _store = store;
            Albums = new List<Album>();

            Initialize();
        }

        public List<Album> Albums { get; private set; }

        /// <summary>
        /// Gets the store backing this catalogue. Used by <see cref="Album"/> and
        /// <see cref="Photo"/> to resolve the public URLs their content is served
        /// from.
        /// </summary>
        internal IPhotoStore Store => _store;

        public bool IsImageFile(string file)
        {
            return PhotoStoreConventions.IsImageFile(file);
        }

        /// <summary>
        /// Gets the public URL the map's marker file is served from, for the home
        /// page to hand to the client-side map script.
        /// </summary>
        /// <returns>The marker file URL.</returns>
        public string MarkersUrl()
        {
            return _store.MarkersUrl();
        }

        /// <summary>
        /// Adds a newly created album and re-sorts the collection.
        /// </summary>
        /// <param name="album">The album to add.</param>
        public void Add(Album album)
        {
            lock (_sync)
            {
                Albums = InDisplayOrder(new List<Album>(Albums) { album });
            }
        }

        /// <summary>
        /// Removes the album whose <see cref="Album.Id"/> matches
        /// <paramref name="id"/>, if it is present.
        /// </summary>
        /// <param name="id">The id (folder name) of the album to remove.</param>
        public void Remove(string id)
        {
            lock (_sync)
            {
                Albums = Albums
                    .Where(a => !a.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>
        /// Reloads a single album from the store (its metadata and its photos) and
        /// swaps the fresh instance into the collection, replacing any existing
        /// album with the same id. This is how an edit that rewrote the album's
        /// metadata is reflected without losing the album's photos.
        /// </summary>
        /// <param name="albumId">The id of the album to reload.</param>
        public void ReloadAlbum(string albumId)
        {
            var reloaded = GetAlbum(albumId);

            lock (_sync)
            {
                var updated = Albums
                    .Where(a => !a.Id.Equals(reloaded.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Albums = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Reflects a completed store rename in the catalogue: the album that was
        /// under <paramref name="oldId"/> is dropped and the moved album is loaded
        /// fresh from the store under <paramref name="newId"/>. Both are swapped in a
        /// single publication so a reader never sees the album twice or not at all.
        /// The store move must already have happened.
        /// </summary>
        /// <param name="oldId">The album's previous id.</param>
        /// <param name="newId">The album's new id.</param>
        public void RenameAlbum(string oldId, string newId)
        {
            var reloaded = GetAlbum(newId);

            lock (_sync)
            {
                var updated = Albums
                    .Where(a => !a.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase)
                        && !a.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Albums = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Rewrites the marker file from the current album set so the map stays in
        /// step after a create, edit or delete. The marker list is snapshotted
        /// under the lock; the store write happens outside it.
        /// </summary>
        /// <returns>A task that completes when the marker file has been written.</returns>
        public async Task WriteMarkersAsync()
        {
            List<Marker> markers;

            lock (_sync)
            {
                markers = Albums
                    .Select(a => new Marker
                    {
                        Lat = a.Latitude,
                        Long = a.Longitude,
                        Slug = a.Id,
                        Name = a.DisplayName,
                        Date = a.Visited.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        Photos = a.Photos.Count,
                        Castle = a.CastleVisited,
                        People = a.People,
                    })
                    .ToList();
            }

            await _store.WriteMarkersAsync(markers);
        }

        private void Initialize()
        {
            var albums = _store.ListAlbumIds()
                .Select(GetAlbum)
                .ToList();

            Albums = InDisplayOrder(albums);
        }

        // Albums are shown newest trip first (by Visited), with the folder id as a
        // stable tie-breaker so albums sharing a date keep a deterministic order.
        private static List<Album> InDisplayOrder(IEnumerable<Album> albums)
        {
            return albums
                .OrderByDescending(a => a.Visited)
                .ThenBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Album GetAlbum(string albumId)
        {
            var metadata = _store.TryReadMetadata(albumId);
            var album = new Album(albumId, this, metadata);

            var photos = _store.ListPhotoFileNames(albumId)
                .Select(fileName => new Photo(album, fileName));

            album.AddPhotos(photos);

            return album;
        }
    }
}