using System.Collections.Generic;

namespace AalgTrips.Models
{
    /// <summary>
    /// A cruise's route as written to <c>cruises.json</c> and read by
    /// <c>wwwroot/js/map.js</c>: an ordered list of ports drawn as a line on the
    /// home map, linking through to the cruise's page. Each port carries the slugs
    /// of the trips done from it so the client can draw a dotted connector to each
    /// trip's own pin. The property names are PascalCase because the client reads
    /// them verbatim from the default-serialized JSON.
    /// </summary>
    public class CruiseRoute
    {
        /// <summary>Gets or sets the cruise's id (slug), used to link through to its page.</summary>
        public string Slug { get; set; }

        /// <summary>Gets or sets the cruise's display name, shown for the route.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the colour the route line, connectors and port pins are drawn
        /// in (a CSS hex colour), so cruises sharing ports are distinguishable.
        /// </summary>
        public string Color { get; set; }

        /// <summary>
        /// Gets or sets the cruise's ports in itinerary order — the vertices of the
        /// route line. Days at sea are not included.
        /// </summary>
        public List<CruisePort> Ports { get; set; }
    }
}