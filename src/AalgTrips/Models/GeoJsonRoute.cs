using System.Collections.Generic;
using System.Text.Json;

namespace AalgTrips.Models
{
    /// <summary>
    /// Parses an uploaded GeoJSON route into the styled <see cref="RouteSegment"/>s the
    /// map draws a journey's line along. The route is computed offline and uploaded per
    /// journey, so no routing runs in the site. A bare <c>LineString</c> becomes one
    /// solid segment; a <c>FeatureCollection</c> becomes one segment per line feature,
    /// each drawn dashed when its feature carries a truthy <c>properties.travel</c> (a
    /// flight or transit hop the traveller did not cover on the ground); a
    /// <c>MultiLineString</c> becomes one segment per line. GeoJSON's
    /// <c>[longitude, latitude]</c> order is flipped to the site's
    /// <c>[latitude, longitude]</c>, and anything malformed or out of range is dropped.
    /// </summary>
    public static class GeoJsonRoute
    {
        // A route is a small hand-off file; cap the total point count so a pathological
        // or hostile upload cannot bloat the stored route or the map payload.
        private const int MaxPoints = 20000;

        /// <summary>
        /// Attempts to parse GeoJSON route text into ordered, styled segments.
        /// </summary>
        /// <param name="json">The uploaded GeoJSON text.</param>
        /// <param name="segments">The parsed segments when parsing succeeds; otherwise empty.</param>
        /// <returns><c>true</c> when at least one usable line (two or more valid points) was read.</returns>
        public static bool TryParse(string json, out List<RouteSegment> segments)
        {
            segments = new List<RouteSegment>();

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
                CollectSegments(document.RootElement, false, segments);
            }

            var total = 0;

            foreach (var segment in segments)
            {
                total += segment.Points.Count;
            }

            // Drop any degenerate segment (a single point is not a line); a usable route
            // needs at least one real segment and must stay within the point cap.
            segments.RemoveAll(s => s.Points.Count < 2);

            return segments.Count > 0 && total <= MaxPoints;
        }

        // Walks a GeoJSON node, unwrapping a FeatureCollection or Feature (reading a
        // truthy properties.travel on the Feature) and appending a segment per line.
        private static void CollectSegments(JsonElement element, bool travel, List<RouteSegment> segments)
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                return;
            }

            switch (typeElement.GetString())
            {
                case "FeatureCollection":
                    if (element.TryGetProperty("features", out var features) && features.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var feature in features.EnumerateArray())
                        {
                            CollectSegments(feature, travel, segments);
                        }
                    }

                    break;

                case "Feature":
                    if (element.TryGetProperty("geometry", out var geometry))
                    {
                        CollectSegments(geometry, travel || IsTravel(element), segments);
                    }

                    break;

                case "LineString":
                    AppendLine(element, travel, segments);
                    break;

                case "MultiLineString":
                    AppendMultiLine(element, travel, segments);
                    break;

                default:
                    break;
            }
        }

        // A Feature is a travel hop when its properties carry a boolean-true "travel".
        private static bool IsTravel(JsonElement feature)
        {
            return feature.TryGetProperty("properties", out var properties)
                && properties.ValueKind == JsonValueKind.Object
                && properties.TryGetProperty("travel", out var flag)
                && flag.ValueKind == JsonValueKind.True;
        }

        private static void AppendLine(JsonElement geometry, bool travel, List<RouteSegment> segments)
        {
            if (!geometry.TryGetProperty("coordinates", out var coordinates)
                || coordinates.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var points = ReadLine(coordinates);

            if (points.Count > 0)
            {
                segments.Add(new RouteSegment { Points = points, Travel = travel });
            }
        }

        private static void AppendMultiLine(JsonElement geometry, bool travel, List<RouteSegment> segments)
        {
            if (!geometry.TryGetProperty("coordinates", out var lines)
                || lines.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var line in lines.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var points = ReadLine(line);

                if (points.Count > 0)
                {
                    segments.Add(new RouteSegment { Points = points, Travel = travel });
                }
            }
        }

        // Reads a line's [ [lon,lat], … ] into ordered [lat,long] pairs, dropping any
        // position that is not a valid coordinate.
        private static List<double[]> ReadLine(JsonElement line)
        {
            var points = new List<double[]>();

            foreach (var position in line.EnumerateArray())
            {
                if (TryReadPosition(position, out var latitude, out var longitude))
                {
                    points.Add(new[] { latitude, longitude });
                }
            }

            return points;
        }

        // Reads one GeoJSON [longitude, latitude(, elevation…)] position, flipping it to
        // [latitude, longitude] and rejecting anything out of geographic range so a
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