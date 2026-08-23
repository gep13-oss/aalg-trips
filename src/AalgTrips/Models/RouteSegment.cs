using System.Collections.Generic;
using System.Text.Json;

namespace AalgTrips.Models
{
    /// <summary>
    /// One drawable segment of a journey's uploaded route: an ordered list of
    /// <c>[latitude, longitude]</c> pairs. A segment is drawn solid for a leg the
    /// traveller covered on the ground (a walked, sailed or driven track), or dashed
    /// when <see cref="Travel"/> is set — a transfer hop they did not cover on the
    /// ground (a flight or long transit, e.g. Beijing → Xi'an). Property names are
    /// PascalCase because the client reads them verbatim from the default-serialized
    /// JSON.
    /// </summary>
    public class RouteSegment
    {
        /// <summary>Gets or sets the ordered <c>[latitude, longitude]</c> pairs of the segment.</summary>
        public List<double[]> Points { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this segment is a travel/transfer
        /// hop (drawn dashed) rather than a covered track (drawn solid).
        /// </summary>
        public bool Travel { get; set; }

        /// <summary>
        /// Reads a stored <c>route.json</c> into segments, tolerating both shapes: the
        /// current list of <c>{ Points, Travel }</c> segments, and the earlier flat
        /// array of <c>[latitude, longitude]</c> pairs (wrapped as one solid segment),
        /// so a route uploaded before segments still draws with no migration.
        /// </summary>
        /// <param name="json">The stored route file contents.</param>
        /// <returns>The segments, or <c>null</c> when there is no usable route.</returns>
        public static List<RouteSegment> FromStoredJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return null;
            }

            var first = root[0];

            // Old shape: a flat array of [lat, lng] pairs — the first element is an
            // array whose first element is a number. Wrap it as one solid segment.
            if (first.ValueKind == JsonValueKind.Array
                && first.GetArrayLength() > 0
                && first[0].ValueKind == JsonValueKind.Number)
            {
                var points = JsonSerializer.Deserialize<List<double[]>>(json);
                return new List<RouteSegment> { new RouteSegment { Points = points, Travel = false } };
            }

            // Current shape: a list of { Points, Travel } segments.
            return JsonSerializer.Deserialize<List<RouteSegment>>(json);
        }
    }
}