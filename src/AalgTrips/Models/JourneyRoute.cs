using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AalgTrips.Models
{
    /// <summary>
    /// A journey's route as written to <c>journeys.json</c> and read by
    /// <c>wwwroot/js/map.js</c>: an ordered list of waypoints drawn as a line on the
    /// home map, linking through to the journey's page. Each waypoint carries the slugs
    /// of the trips done from it so the client can draw a dotted connector to each
    /// trip's own pin. The property names are PascalCase because the client reads them
    /// verbatim from the default-serialized JSON.
    /// </summary>
    public class JourneyRoute
    {
        /// <summary>Gets or sets the journey's id (slug), used to link through to its page.</summary>
        public string Slug { get; set; }

        /// <summary>Gets or sets the journey's display name, shown for the route.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the journey's kind, so the home page can show or hide a kind's
        /// routes together with its card section. Written as its name (e.g.
        /// <c>"Cruise"</c>) so it matches the <c>data-kind</c> the client toggles by.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public JourneyKind Kind { get; set; }

        /// <summary>
        /// Gets or sets the colour the route line, connectors and waypoint pins are
        /// drawn in (a CSS hex colour), so journeys sharing stops are distinguishable.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Gets or sets the journey's located stops in itinerary order — the vertices
        /// of the route line and the waypoint pins. Travel days are not included.
        /// </summary>
        public List<JourneyWaypoint> Waypoints { get; set; }

        /// <summary>
        /// Gets or sets the optional pre-computed route geometry the line is drawn
        /// along — an ordered list of styled <see cref="RouteSegment"/>s uploaded for
        /// the journey (solid for a covered track, dashed for a travel hop such as a
        /// flight). <c>null</c> when no route has been uploaded, in which case the
        /// client draws straight lines between the <see cref="Waypoints"/> instead.
        /// </summary>
        public List<RouteSegment> Geometry { get; set; }
    }
}