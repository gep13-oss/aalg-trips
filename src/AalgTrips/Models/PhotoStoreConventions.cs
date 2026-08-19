using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AalgTrips.Models
{
    /// <summary>
    /// The naming and layout conventions shared by every <see cref="IPhotoStore"/>
    /// implementation, so a local-disk store and an Azure Blob store agree on the
    /// same key structure, thumbnail naming, and public URL encoding. Keys are
    /// always <c>{albumId}/…</c>: <c>{albumId}/data.json</c> for metadata,
    /// <c>{albumId}/{photo}</c> for an original, and
    /// <c>{albumId}/thumbnail/{thumb}</c> for a generated thumbnail; the marker
    /// file sits at the top level as <c>markers.json</c>.
    /// </summary>
    public static class PhotoStoreConventions
    {
        /// <summary>The sub-folder (key segment) generated thumbnails live under.</summary>
        public const string ThumbnailFolder = "thumbnail";

        /// <summary>The file name an album's metadata is stored as.</summary>
        public const string MetadataFileName = "data.json";

        /// <summary>The file name the map's marker list is stored as.</summary>
        public const string MarkersFileName = "markers.json";

        /// <summary>
        /// The sub-folder (key prefix) all cruise content lives under, so cruises
        /// are stored beside albums yet kept out of the album catalogue.
        /// </summary>
        public const string CruisesFolder = "cruises";

        /// <summary>The file name a cruise's metadata is stored as.</summary>
        public const string CruiseMetadataFileName = "cruise.json";

        /// <summary>The file name the map's cruise-route list is stored as (at the top level).</summary>
        public const string CruisesFileName = "cruises.json";

        private static readonly string[] _imageExtensions = { ".jpg", ".jpeg", ".gif", ".png" };
        private static readonly Regex _thumbnailSuffix = new Regex(@"-[0-9]+x[0-9]+$", RegexOptions.Compiled);

        /// <summary>
        /// Determines whether a file name is one of the image types the site
        /// accepts (by extension).
        /// </summary>
        /// <param name="fileName">The file name to test.</param>
        /// <returns><c>true</c> when the extension is a recognised image type.</returns>
        public static bool IsImageFile(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            return _imageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The root-relative base path the authenticated media endpoint is served
        /// under. Album content is no longer a public static file; it is streamed
        /// through this endpoint so it is only reachable by a signed-in user.
        /// </summary>
        public const string MediaBase = "/albums";

        /// <summary>
        /// Gets the URL an original photo is served from.
        /// </summary>
        /// <param name="albumId">The album containing the photo.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>The root-relative media URL.</returns>
        public static string PhotoUrl(string albumId, string fileName)
        {
            return $"{MediaBase}/{Escape(albumId)}/{Escape(fileName)}";
        }

        /// <summary>
        /// Gets the URL a thumbnail is served from.
        /// </summary>
        /// <param name="albumId">The album containing the thumbnail.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <returns>The root-relative media URL.</returns>
        public static string ThumbnailUrl(string albumId, string thumbnailFileName)
        {
            return $"{MediaBase}/{Escape(albumId)}/{ThumbnailFolder}/{Escape(thumbnailFileName)}";
        }

        /// <summary>
        /// Gets the URL the map's marker file is served from.
        /// </summary>
        /// <returns>The root-relative media URL.</returns>
        public static string MarkersUrl()
        {
            return $"{MediaBase}/{MarkersFileName}";
        }

        /// <summary>
        /// Gets the URL the map's cruise-route file is served from.
        /// </summary>
        /// <returns>The root-relative media URL.</returns>
        public static string CruisesUrl()
        {
            return $"{MediaBase}/{CruisesFileName}";
        }

        /// <summary>
        /// Gets the URL a cruise stop's original photo is served from. Cruise photos
        /// live under the cruises area (<c>cruises/{cruiseId}/{stopKey}/{file}</c>),
        /// which the same authenticated media endpoint serves as any other store key.
        /// </summary>
        /// <param name="cruiseId">The cruise the photo belongs to.</param>
        /// <param name="stopKey">The stop the photo belongs to.</param>
        /// <param name="fileName">The photo file name.</param>
        /// <returns>The root-relative media URL.</returns>
        public static string CruisePhotoUrl(string cruiseId, string stopKey, string fileName)
        {
            return $"{MediaBase}/{CruisesFolder}/{Escape(cruiseId)}/{Escape(stopKey)}/{Escape(fileName)}";
        }

        /// <summary>
        /// Gets the URL a cruise stop's thumbnail is served from.
        /// </summary>
        /// <param name="cruiseId">The cruise the thumbnail belongs to.</param>
        /// <param name="stopKey">The stop the thumbnail belongs to.</param>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <returns>The root-relative media URL.</returns>
        public static string CruiseThumbnailUrl(string cruiseId, string stopKey, string thumbnailFileName)
        {
            return $"{MediaBase}/{CruisesFolder}/{Escape(cruiseId)}/{Escape(stopKey)}/{ThumbnailFolder}/{Escape(thumbnailFileName)}";
        }

        // Percent-encodes a single path segment (album id or file name) so it round
        // trips through the media endpoint's catch-all route back to the exact
        // store key. The separators between segments are added literally.
        private static string Escape(string segment)
        {
            return Uri.EscapeDataString(segment);
        }

        /// <summary>
        /// Determines whether a thumbnail file name was generated from a given
        /// original photo — i.e. it is <c>{name}-{width}x{height}{ext}</c>.
        /// </summary>
        /// <param name="thumbnailFileName">The thumbnail file name.</param>
        /// <param name="photoFileName">The original photo file name.</param>
        /// <returns><c>true</c> when the thumbnail belongs to the photo.</returns>
        public static bool ThumbnailBelongsTo(string thumbnailFileName, string photoFileName)
        {
            string photoName = Path.GetFileNameWithoutExtension(photoFileName);
            string photoExt = Path.GetExtension(photoFileName);

            if (!thumbnailFileName.EndsWith(photoExt, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string thumbName = Path.GetFileNameWithoutExtension(thumbnailFileName);

            // Must be the photo's name followed by a "-{width}x{height}" suffix.
            if (!thumbName.StartsWith(photoName + "-", StringComparison.Ordinal))
            {
                return false;
            }

            string suffix = thumbName.Substring(photoName.Length);
            return _thumbnailSuffix.IsMatch(suffix);
        }

        /// <summary>
        /// Produces the new name a thumbnail should take when its original photo
        /// is renamed, preserving the <c>-{width}x{height}{ext}</c> suffix.
        /// </summary>
        /// <param name="thumbnailFileName">The current thumbnail file name.</param>
        /// <param name="oldPhotoFileName">The original photo's current file name.</param>
        /// <param name="newPhotoFileName">The original photo's new file name.</param>
        /// <returns>The renamed thumbnail file name.</returns>
        public static string RenameThumbnail(string thumbnailFileName, string oldPhotoFileName, string newPhotoFileName)
        {
            string oldName = Path.GetFileNameWithoutExtension(oldPhotoFileName);
            string newName = Path.GetFileNameWithoutExtension(newPhotoFileName);

            // The thumbnail starts with the old photo name; swap just that prefix
            // so the "-{width}x{height}{ext}" tail is preserved verbatim.
            return newName + thumbnailFileName.Substring(oldName.Length);
        }
    }
}