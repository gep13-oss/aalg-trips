using System;
using System.Collections.Generic;

namespace AalgTrips.Models
{
    /// <summary>
    /// The persisted details of a cruise, stored as <c>cruise.json</c> under the
    /// cruise's folder. A cruise groups an ordered itinerary of ports (and days at
    /// sea) with links out to the trip albums visited along the way. Unlike an
    /// album it has no single location; it is drawn on the map as a route through
    /// its ports.
    /// </summary>
    public class CruiseMetaData
    {
        /// <summary>Gets or sets the cruise's display name.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the cruise's free-text description / notes.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the date the cruise departed.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Gets or sets the date the cruise returned.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Gets or sets the colour the cruise's route is drawn in on the home map (a
        /// CSS hex colour, e.g. <c>#0e6e78</c>), so cruises that share ports can be
        /// told apart. Absent from older metadata, where it deserializes to
        /// <c>null</c> and falls back to the default route colour.
        /// </summary>
        public string RouteColor { get; set; }

        /// <summary>
        /// Gets or sets the people who were on the cruise (free-text names, as on an
        /// album). Absent from older metadata, where it deserializes to <c>null</c>
        /// and is treated as an empty list.
        /// </summary>
        public List<string> People { get; set; }

        /// <summary>
        /// Gets or sets the cruise's itinerary, in order. Each entry is a port call
        /// or a day at sea. Absent from older metadata, where it deserializes to
        /// <c>null</c> and is treated as an empty list.
        /// </summary>
        public List<CruiseStop> Stops { get; set; }
    }
}