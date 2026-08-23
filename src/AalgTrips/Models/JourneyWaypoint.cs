using System.Collections.Generic;

namespace AalgTrips.Models
{
    /// <summary>
    /// A single port on a <see cref="JourneyRoute"/>: a waypoint the route line
    /// passes through, carrying the fields the map tooltip shows and the slugs of
    /// the trips visited from it (for the dotted connectors). PascalCase so the
    /// client reads it verbatim from the serialized JSON.
    /// </summary>
    public class JourneyWaypoint
    {
        /// <summary>Gets or sets the port's latitude.</summary>
        public double Lat { get; set; }

        /// <summary>Gets or sets the port's longitude.</summary>
        public double Long { get; set; }

        /// <summary>Gets or sets the port's name, shown as the tooltip heading.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the stop date pre-formatted for display (e.g. <c>29 Jul 2025</c>),
        /// in the invariant culture so the client needs no date parsing.
        /// </summary>
        public string Date { get; set; }

        /// <summary>Gets or sets the arrival time shown in the tooltip, or <c>null</c>.</summary>
        public string Arrive { get; set; }

        /// <summary>Gets or sets the departure time shown in the tooltip, or <c>null</c>.</summary>
        public string Depart { get; set; }

        /// <summary>
        /// Gets or sets the ids (slugs) of the trips visited from this port, so the
        /// client can draw a dotted connector to each trip's own pin.
        /// </summary>
        public List<string> Trips { get; set; }
    }
}