using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace AalgTrips.Models
{
    /// <summary>
    /// A single photo saved against a journey stop, projected for display. It mirrors
    /// <see cref="Photo"/>'s thumbnail resolution — matching the
    /// <c>{name}-{width}x{height}{ext}</c> convention and caching the resolved height
    /// per width — but is bound to a journey stop (store, journey id and stop key)
    /// rather than an album, since a journey's photos live under
    /// <c>journeys/{journeyId}/{stopKey}/</c> and have no per-photo page.
    /// </summary>
    public class JourneyPhoto
    {
        private static readonly Regex _size = new Regex(@"-(?<width>[0-9]+)x(?<height>[0-9]+)\.", RegexOptions.Compiled);
        private readonly IPhotoStore _store;
        private readonly string _journeyId;
        private readonly string _stopKey;
        private readonly Dictionary<int, int> _heights = new Dictionary<int, int>();

        public JourneyPhoto(IPhotoStore store, string journeyId, string stopKey, string fileName)
        {
            _store = store;
            _journeyId = journeyId;
            _stopKey = stopKey;
            Id = fileName;
        }

        /// <summary>Gets the photo's file name (its id within the stop).</summary>
        public string Id { get; }

        /// <summary>Gets the photo's display name (the file name without extension).</summary>
        public string DisplayName => Path.GetFileNameWithoutExtension(Id);

        /// <summary>Gets the served URL of the original photo.</summary>
        public string PhotoUrl => _store.JourneyPhotoUrl(_journeyId, _stopKey, Id);

        /// <summary>
        /// Resolves the served URL of the thumbnail generated at the given width and
        /// reports its height, or <c>null</c> (and <c>0</c>) when no thumbnail of that
        /// width exists.
        /// </summary>
        /// <param name="width">The thumbnail width to resolve.</param>
        /// <param name="height">The resolved thumbnail height, or <c>0</c> when there is none.</param>
        /// <returns>The thumbnail URL, or <c>null</c> when no thumbnail of that width exists.</returns>
        public string GetThumbnailLink(int width, out int height)
        {
            if (_heights.TryGetValue(width, out height))
            {
                return _store.JourneyThumbnailUrl(_journeyId, _stopKey, ThumbnailFileName(width, height));
            }

            foreach (var thumbnail in _store.ListJourneyThumbnailFileNames(_journeyId, _stopKey))
            {
                if (!PhotoStoreConventions.ThumbnailBelongsTo(thumbnail, Id))
                {
                    continue;
                }

                Match match = _size.Match(thumbnail);

                if (match.Success && int.Parse(match.Groups["width"].Value) == width)
                {
                    height = int.Parse(match.Groups["height"].Value);
                    _heights[width] = height;
                    return _store.JourneyThumbnailUrl(_journeyId, _stopKey, thumbnail);
                }
            }

            height = 0;
            return null;
        }

        private string ThumbnailFileName(int width, int height)
        {
            string ext = Path.GetExtension(Id);
            return $"{DisplayName}-{width}x{height}{ext}";
        }
    }
}