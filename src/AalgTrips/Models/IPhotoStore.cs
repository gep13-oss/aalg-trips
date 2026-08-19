using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// Abstracts where album content (photos, generated thumbnails, album
    /// metadata and the map's <c>markers.json</c>) is stored and served from.
    /// The app keeps its filesystem-as-content model conceptually — albums are
    /// keyed by a folder-like <c>albumId</c> and photos by file name — but the
    /// backing store is pluggable: a local disk implementation for development
    /// and tests, and an Azure Blob implementation for production, selected by
    /// configuration. The store also provides the public URL each photo,
    /// thumbnail and the marker file is served from, so a production deployment
    /// can serve them straight from a CDN while local serving stays under
    /// <c>/albums</c>.
    /// </summary>
    public interface IPhotoStore
    {
        /// <summary>
        /// Lists the ids (folder-like names) of every album currently in the
        /// store. Used to build the in-memory catalogue at start-up.
        /// </summary>
        /// <returns>The album ids, in no particular order.</returns>
        IReadOnlyList<string> ListAlbumIds();

        /// <summary>
        /// Reads an album's metadata (its <c>data.json</c>) if present.
        /// </summary>
        /// <param name="albumId">The album to read.</param>
        /// <returns>The metadata, or <c>null</c> when the album has none.</returns>
        AlbumMetaData TryReadMetadata(string albumId);

        /// <summary>
        /// Lists the original photo file names in an album (not thumbnails).
        /// </summary>
        /// <param name="albumId">The album to enumerate.</param>
        /// <returns>The photo file names.</returns>
        IReadOnlyList<string> ListPhotoFileNames(string albumId);

        /// <summary>
        /// Lists the generated thumbnail file names in an album.
        /// </summary>
        /// <param name="albumId">The album to enumerate.</param>
        /// <returns>The thumbnail file names (e.g. <c>beach-190x127.jpg</c>).</returns>
        IReadOnlyList<string> ListThumbnailFileNames(string albumId);

        /// <summary>
        /// Determines whether an album exists in the store.
        /// </summary>
        /// <param name="albumId">The album to check.</param>
        /// <returns><c>true</c> when the album exists.</returns>
        bool AlbumExists(string albumId);

        /// <summary>
        /// Determines whether a photo exists in an album.
        /// </summary>
        /// <param name="albumId">The album to check.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns><c>true</c> when the photo exists.</returns>
        bool PhotoExists(string albumId, string fileName);

        /// <summary>
        /// Writes (creates or replaces) an album's metadata.
        /// </summary>
        /// <param name="albumId">The album to write.</param>
        /// <param name="metadata">The metadata to store.</param>
        /// <returns>A task that completes when the metadata is stored.</returns>
        Task WriteMetadataAsync(string albumId, AlbumMetaData metadata);

        /// <summary>
        /// Deletes an album and all of its content (photos, thumbnails,
        /// metadata).
        /// </summary>
        /// <param name="albumId">The album to delete.</param>
        /// <returns>A task that completes when the album is removed.</returns>
        Task DeleteAlbumAsync(string albumId);

        /// <summary>
        /// Saves an original photo's bytes into an album.
        /// </summary>
        /// <param name="albumId">The album to save into.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <param name="content">A readable stream over the photo bytes.</param>
        /// <returns>A task that completes when the photo is stored.</returns>
        Task SavePhotoAsync(string albumId, string fileName, Stream content);

        /// <summary>
        /// Opens a stored photo for reading (for example to derive thumbnails
        /// from the saved original).
        /// </summary>
        /// <param name="albumId">The album to read from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A readable stream the caller must dispose.</returns>
        Stream OpenPhoto(string albumId, string fileName);

        /// <summary>
        /// Saves a generated thumbnail's bytes into an album.
        /// </summary>
        /// <param name="albumId">The album to save into.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <param name="content">A readable stream over the thumbnail bytes.</param>
        /// <returns>A task that completes when the thumbnail is stored.</returns>
        Task SaveThumbnailAsync(string albumId, string thumbnailFileName, Stream content);

        /// <summary>
        /// Deletes a photo and every thumbnail generated from it.
        /// </summary>
        /// <param name="albumId">The album to delete from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A task that completes when the photo and its thumbnails are removed.</returns>
        Task DeletePhotoAsync(string albumId, string fileName);

        /// <summary>
        /// Renames a photo and every thumbnail generated from it, preserving the
        /// thumbnail size suffixes.
        /// </summary>
        /// <param name="albumId">The album containing the photo.</param>
        /// <param name="oldFileName">The current photo file name.</param>
        /// <param name="newFileName">The new photo file name.</param>
        /// <returns>A task that completes when the photo and its thumbnails are renamed.</returns>
        Task RenamePhotoAsync(string albumId, string oldFileName, string newFileName);

        /// <summary>
        /// Renames an album, moving all of its content (metadata, photos and
        /// thumbnails) from <paramref name="oldAlbumId"/> to
        /// <paramref name="newAlbumId"/>. The caller guarantees the new id is free.
        /// </summary>
        /// <param name="oldAlbumId">The album's current id.</param>
        /// <param name="newAlbumId">The album's new id.</param>
        /// <returns>A task that completes when the album has been moved.</returns>
        Task RenameAlbumAsync(string oldAlbumId, string newAlbumId);

        /// <summary>
        /// Rewrites the map's marker file from the supplied markers.
        /// </summary>
        /// <param name="markers">One marker per album with coordinates.</param>
        /// <returns>A task that completes when the marker file is stored.</returns>
        Task WriteMarkersAsync(IEnumerable<Marker> markers);

        /// <summary>
        /// Lists the ids (folder-like names) of every cruise currently in the
        /// store. Used to build the in-memory cruise catalogue at start-up.
        /// </summary>
        /// <returns>The cruise ids, in no particular order.</returns>
        IReadOnlyList<string> ListCruiseIds();

        /// <summary>
        /// Reads a cruise's metadata (its <c>cruise.json</c>) if present.
        /// </summary>
        /// <param name="cruiseId">The cruise to read.</param>
        /// <returns>The metadata, or <c>null</c> when the cruise has none.</returns>
        CruiseMetaData TryReadCruise(string cruiseId);

        /// <summary>
        /// Determines whether a cruise exists in the store.
        /// </summary>
        /// <param name="cruiseId">The cruise to check.</param>
        /// <returns><c>true</c> when the cruise exists.</returns>
        bool CruiseExists(string cruiseId);

        /// <summary>
        /// Writes (creates or replaces) a cruise's metadata.
        /// </summary>
        /// <param name="cruiseId">The cruise to write.</param>
        /// <param name="metadata">The metadata to store.</param>
        /// <returns>A task that completes when the metadata is stored.</returns>
        Task WriteCruiseAsync(string cruiseId, CruiseMetaData metadata);

        /// <summary>
        /// Deletes a cruise and all of its content (metadata and, later, its
        /// per-day photos).
        /// </summary>
        /// <param name="cruiseId">The cruise to delete.</param>
        /// <returns>A task that completes when the cruise is removed.</returns>
        Task DeleteCruiseAsync(string cruiseId);

        /// <summary>
        /// Renames a cruise, moving all of its content from
        /// <paramref name="oldCruiseId"/> to <paramref name="newCruiseId"/>. The
        /// caller guarantees the new id is free.
        /// </summary>
        /// <param name="oldCruiseId">The cruise's current id.</param>
        /// <param name="newCruiseId">The cruise's new id.</param>
        /// <returns>A task that completes when the cruise has been moved.</returns>
        Task RenameCruiseAsync(string oldCruiseId, string newCruiseId);

        /// <summary>
        /// Rewrites the map's cruise-route file from the supplied routes.
        /// </summary>
        /// <param name="routes">One route per cruise.</param>
        /// <returns>A task that completes when the cruise-route file is stored.</returns>
        Task WriteCruisesAsync(IEnumerable<CruiseRoute> routes);

        /// <summary>
        /// Opens stored content by its store key (for example
        /// <c>{albumId}/{photo}</c>, <c>{albumId}/thumbnail/{thumb}</c> or
        /// <c>markers.json</c>) so the authenticated media endpoint can stream it.
        /// This is how photos, thumbnails and the marker file are served now that
        /// they are private and no longer public static files.
        /// </summary>
        /// <param name="key">The store key of the content, using <c>/</c> separators.</param>
        /// <param name="content">The readable stream when the method returns <c>true</c>; the caller disposes it.</param>
        /// <returns><c>true</c> when the content exists and was opened.</returns>
        bool TryOpenContent(string key, out Stream content);

        /// <summary>
        /// Gets the URL an original photo is served from (the authenticated media
        /// endpoint).
        /// </summary>
        /// <param name="albumId">The album containing the photo.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>The root-relative URL of the photo.</returns>
        string PhotoUrl(string albumId, string fileName);

        /// <summary>
        /// Gets the public URL a thumbnail is served from.
        /// </summary>
        /// <param name="albumId">The album containing the thumbnail.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <returns>The absolute-or-root-relative URL of the thumbnail.</returns>
        string ThumbnailUrl(string albumId, string thumbnailFileName);

        /// <summary>
        /// Gets the public URL the map's marker file is served from.
        /// </summary>
        /// <returns>The absolute-or-root-relative URL of <c>markers.json</c>.</returns>
        string MarkersUrl();

        /// <summary>
        /// Gets the public URL the map's cruise-route file is served from.
        /// </summary>
        /// <returns>The absolute-or-root-relative URL of <c>cruises.json</c>.</returns>
        string CruisesUrl();

        /// <summary>
        /// Lists the original photo file names saved for a cruise stop (not
        /// thumbnails).
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to enumerate.</param>
        /// <returns>The photo file names.</returns>
        IReadOnlyList<string> ListCruisePhotoFileNames(string cruiseId, string stopKey);

        /// <summary>
        /// Lists the generated thumbnail file names for a cruise stop.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to enumerate.</param>
        /// <returns>The thumbnail file names (e.g. <c>beach-190x127.jpg</c>).</returns>
        IReadOnlyList<string> ListCruiseThumbnailFileNames(string cruiseId, string stopKey);

        /// <summary>
        /// Determines whether a photo exists for a cruise stop.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to check.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns><c>true</c> when the photo exists.</returns>
        bool CruisePhotoExists(string cruiseId, string stopKey, string fileName);

        /// <summary>
        /// Saves an original photo's bytes into a cruise stop.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to save into.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <param name="content">A readable stream over the photo bytes.</param>
        /// <returns>A task that completes when the photo is stored.</returns>
        Task SaveCruisePhotoAsync(string cruiseId, string stopKey, string fileName, Stream content);

        /// <summary>
        /// Opens a stored cruise-stop photo for reading (for example to derive
        /// thumbnails from the saved original).
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to read from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A readable stream the caller must dispose.</returns>
        Stream OpenCruisePhoto(string cruiseId, string stopKey, string fileName);

        /// <summary>
        /// Saves a generated thumbnail's bytes into a cruise stop.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to save into.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <param name="content">A readable stream over the thumbnail bytes.</param>
        /// <returns>A task that completes when the thumbnail is stored.</returns>
        Task SaveCruiseThumbnailAsync(string cruiseId, string stopKey, string thumbnailFileName, Stream content);

        /// <summary>
        /// Deletes a cruise-stop photo and every thumbnail generated from it.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop to delete from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A task that completes when the photo and its thumbnails are removed.</returns>
        Task DeleteCruisePhotoAsync(string cruiseId, string stopKey, string fileName);

        /// <summary>
        /// Gets the URL a cruise stop's original photo is served from.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop the photo belongs to.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>The root-relative URL of the photo.</returns>
        string CruisePhotoUrl(string cruiseId, string stopKey, string fileName);

        /// <summary>
        /// Gets the URL a cruise stop's thumbnail is served from.
        /// </summary>
        /// <param name="cruiseId">The cruise the stop belongs to.</param>
        /// <param name="stopKey">The stop the thumbnail belongs to.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <returns>The absolute-or-root-relative URL of the thumbnail.</returns>
        string CruiseThumbnailUrl(string cruiseId, string stopKey, string thumbnailFileName);
    }
}