using System.Collections.Generic;
using System.Text.Json;

namespace AalgTrips.Models
{
    /// <summary>
    /// Parses an uploaded GeoJSON route into the ordered <c>[latitude, longitude]</c>
    /// pairs the map draws a cruise's line along. The route is computed offline (a
    /// rough sea path that stays on water) and uploaded per cruise, so no marine
    /// routing runs in the site; this only reads a <c>LineString</c> (or a
    /// <c>MultiLineString</c>, flattened in order) out of a GeoJSON <c>Feature</c>,
    /// <c>FeatureCollection</c> or bare geometry, flips GeoJSON's
    /// <c>[longitude, latitude]</c> order to the site's <c>[latitude, longitude]</c>,
    /// and rejects anything malformed or out of geographic range.
    /// </summary>
    public static class GeoJsonRoute
    {
        // A route is a small hand-off file; cap the point count so a pathological or
        // hostile upload cannot bloat the stored route or the map payload.
        private const int MaxPoints = 20000;

        /// <summary>
        /// Attempts to parse GeoJSON route text into ordered latitude/longitude pairs.
        /// </summary>
        /// <param name="json">The uploaded GeoJSON text.</param>
        /// <param name="points">The parsed <c>[latitude, longitude]</c> pairs when parsing succeeds; otherwise empty.</param>
        /// <returns><c>true</c> when a usable line of at least two valid points was read.</returns>
        public static bool TryParse(string json, out List<double[]> points)
        {
            points = new List<double[]>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            JsonDocument document;

            try
            {
                document = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return false;
            }

            using (document)
            {
                if (!TryFindLineGeometry(document.RootElement, out var geometry))
                {
                    return false;
                }

                CollectLinePoints(geometry, points);
            }

            // A line needs at least two points; guard the upper bound too.
            return points.Count >= 2 && points.Count <= MaxPoints;
        }

        // Locates the first LineString/MultiLineString geometry, unwrapping a
        // FeatureCollection (its first suitable feature) or a Feature as needed.
        private static bool TryFindLineGeometry(JsonElement element, out JsonElement geometry)
        {
            geometry = default;

            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            switch (typeElement.GetString())
            {
                case "LineString":
                case "MultiLineString":
                    geometry = element;
                    return true;

                case "Feature":
                    return element.TryGetProperty("geometry", out var featureGeometry)
                        && TryFindLineGeometry(featureGeometry, out geometry);

                case "FeatureCollection":
                    if (element.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var feature in features.EnumerateArray())
                        {
                            if (TryFindLineGeometry(feature, out geometry))
                            {
                                return true;
                            }
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }

        // Appends every valid coordinate from a LineString's [ [lon,lat], … ] or a
        // MultiLineString's [ [ [lon,lat], … ], … ] to the result, preserving order.
        private static void CollectLinePoints(JsonElement geometry, List<double[]> points)
        {
            if (!geometry.TryGetProperty("type", out var typeElement)
                || !geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            if (typeElement.GetString() == "MultiLineString")
            {
                foreach (var line in coordinates.EnumerateArray())
                {
                    AppendLine(line, points);
                }
            }
            else
            {
                AppendLine(coordinates, points);
            }
        }

        private static void AppendLine(JsonElement line, List<double[]> points)
        {
            if (line.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var position in line.EnumerateArray())
            {
                if (TryReadPosition(position, out var latitude, out var longitude))
                {
                    points.Add(new[] { latitude, longitude });
                }
            }
        }

        // Reads one GeoJSON [longitude, latitude(, elevation…)] position, flipping it
        // to [latitude, longitude] and rejecting anything out of geographic range so a
        // swapped or garbage coordinate never reaches the map.
        private static bool TryReadPosition(JsonElement position, out double latitude, out double longitude)
        {
            latitude = 0;
            longitude = 0;

            if (position.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var enumerator = position.EnumerateArray();

            if (!enumerator.MoveNext() || enumerator.Current.ValueKind != JsonValueKind.Number
                || !enumerator.Current.TryGetDouble(out longitude))
            {
                return false;
            }

            if (!enumerator.MoveNext() || enumerator.Current.ValueKind != JsonValueKind.Number
                || !enumerator.Current.TryGetDouble(out latitude))
            {
                return false;
            }

            return longitude >= -180 && longitude <= 180 && latitude >= -90 && latitude <= 90;
        }
    }
}