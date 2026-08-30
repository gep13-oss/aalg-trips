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
        /// Lists the ids (folder-like names) of every journey currently in the
        /// store. Used to build the in-memory journey catalogue at start-up.
        /// </summary>
        /// <returns>The journey ids, in no particular order.</returns>
        IReadOnlyList<string> ListJourneyIds();

        /// <summary>
        /// Reads a journey's metadata (its <c>journey.json</c>) if present.
        /// </summary>
        /// <param name="journeyId">The journey to read.</param>
        /// <returns>The metadata, or <c>null</c> when the journey has none.</returns>
        JourneyMetaData TryReadJourney(string journeyId);

        /// <summary>
        /// Determines whether a journey exists in the store.
        /// </summary>
        /// <param name="journeyId">The journey to check.</param>
        /// <returns><c>true</c> when the journey exists.</returns>
        bool JourneyExists(string journeyId);

        /// <summary>
        /// Writes (creates or replaces) a journey's metadata.
        /// </summary>
        /// <param name="journeyId">The journey to write.</param>
        /// <param name="metadata">The metadata to store.</param>
        /// <returns>A task that completes when the metadata is stored.</returns>
        Task WriteJourneyAsync(string journeyId, JourneyMetaData metadata);

        /// <summary>
        /// Deletes a journey and all of its content (metadata and, later, its
        /// per-day photos).
        /// </summary>
        /// <param name="journeyId">The journey to delete.</param>
        /// <returns>A task that completes when the journey is removed.</returns>
        Task DeleteJourneyAsync(string journeyId);

        /// <summary>
        /// Renames a journey, moving all of its content from
        /// <paramref name="oldJourneyId"/> to <paramref name="newJourneyId"/>. The
        /// caller guarantees the new id is free.
        /// </summary>
        /// <param name="oldJourneyId">The journey's current id.</param>
        /// <param name="newJourneyId">The journey's new id.</param>
        /// <returns>A task that completes when the journey has been moved.</returns>
        Task RenameJourneyAsync(string oldJourneyId, string newJourneyId);

        /// <summary>
        /// Rewrites the map's journey-route file from the supplied routes.
        /// </summary>
        /// <param name="routes">One route per journey.</param>
        /// <returns>A task that completes when the journey-route file is stored.</returns>
        Task WriteJourneysAsync(IEnumerable<JourneyRoute> routes);

        /// <summary>
        /// Reads the set of castle ids that have been explicitly marked as visited on
        /// the "Castle Bingo" page. Returns an empty list when none have been marked.
        /// </summary>
        /// <returns>The marked-visited castle ids.</returns>
        IReadOnlyList<string> ReadVisitedCastles();

        /// <summary>
        /// Rewrites the set of explicitly-marked-visited castle ids.
        /// </summary>
        /// <param name="castleIds">The castle ids currently marked as visited.</param>
        /// <returns>A task that completes when the set is stored.</returns>
        Task WriteVisitedCastlesAsync(IEnumerable<string> castleIds);

        /// <summary>
        /// Reads a journey's uploaded route geometry if present — the ordered, styled
        /// <see cref="RouteSegment"/>s the map draws the journey's line along (a track
        /// computed offline and uploaded per journey). Tolerates a route uploaded before
        /// segments (a flat point list) by returning it as one solid segment.
        /// </summary>
        /// <param name="journeyId">The journey to read.</param>
        /// <returns>The route segments, or <c>null</c> when the journey has no uploaded route.</returns>
        IReadOnlyList<RouteSegment> TryReadJourneyRoute(string journeyId);

        /// <summary>
        /// Saves (creates or replaces) a journey's route geometry.
        /// </summary>
        /// <param name="journeyId">The journey to write.</param>
        /// <param name="route">The ordered, styled route segments.</param>
        /// <returns>A task that completes when the route is stored.</returns>
        Task SaveJourneyRouteAsync(string journeyId, IEnumerable<RouteSegment> route);

        /// <summary>
        /// Deletes a journey's uploaded route geometry, reverting its map line to
        /// straight stop-to-stop segments. A no-op when the journey has no route.
        /// </summary>
        /// <param name="journeyId">The journey whose route is removed.</param>
        /// <returns>A task that completes when the route is removed.</returns>
        Task DeleteJourneyRouteAsync(string journeyId);

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
        /// Gets the public URL the map's journey-route file is served from.
        /// </summary>
        /// <returns>The absolute-or-root-relative URL of <c>journeys.json</c>.</returns>
        string JourneysUrl();

        /// <summary>
        /// Lists the original photo file names saved for a journey stop (not
        /// thumbnails).
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to enumerate.</param>
        /// <returns>The photo file names.</returns>
        IReadOnlyList<string> ListJourneyPhotoFileNames(string journeyId, string stopKey);

        /// <summary>
        /// Lists the generated thumbnail file names for a journey stop.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to enumerate.</param>
        /// <returns>The thumbnail file names (e.g. <c>beach-190x127.jpg</c>).</returns>
        IReadOnlyList<string> ListJourneyThumbnailFileNames(string journeyId, string stopKey);

        /// <summary>
        /// Determines whether a photo exists for a journey stop.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to check.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns><c>true</c> when the photo exists.</returns>
        bool JourneyPhotoExists(string journeyId, string stopKey, string fileName);

        /// <summary>
        /// Saves an original photo's bytes into a journey stop.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to save into.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <param name="content">A readable stream over the photo bytes.</param>
        /// <returns>A task that completes when the photo is stored.</returns>
        Task SaveJourneyPhotoAsync(string journeyId, string stopKey, string fileName, Stream content);

        /// <summary>
        /// Opens a stored journey-stop photo for reading (for example to derive
        /// thumbnails from the saved original).
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to read from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A readable stream the caller must dispose.</returns>
        Stream OpenJourneyPhoto(string journeyId, string stopKey, string fileName);

        /// <summary>
        /// Saves a generated thumbnail's bytes into a journey stop.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to save into.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <param name="content">A readable stream over the thumbnail bytes.</param>
        /// <returns>A task that completes when the thumbnail is stored.</returns>
        Task SaveJourneyThumbnailAsync(string journeyId, string stopKey, string thumbnailFileName, Stream content);

        /// <summary>
        /// Deletes a journey-stop photo and every thumbnail generated from it.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop to delete from.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>A task that completes when the photo and its thumbnails are removed.</returns>
        Task DeleteJourneyPhotoAsync(string journeyId, string stopKey, string fileName);

        /// <summary>
        /// Gets the URL a journey stop's original photo is served from.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop the photo belongs to.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>The root-relative URL of the photo.</returns>
        string JourneyPhotoUrl(string journeyId, string stopKey, string fileName);

        /// <summary>
        /// Gets the URL a journey stop's thumbnail is served from.
        /// </summary>
        /// <param name="journeyId">The journey the stop belongs to.</param>
        /// <param name="stopKey">The stop the thumbnail belongs to.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <returns>The absolute-or-root-relative URL of the thumbnail.</returns>
        string JourneyThumbnailUrl(string journeyId, string stopKey, string thumbnailFileName);
    }
}