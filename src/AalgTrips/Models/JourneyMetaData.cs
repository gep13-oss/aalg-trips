using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AalgTrips.Models
{
    /// <summary>
    /// The persisted details of a journey, stored as <c>journey.json</c> under the
    /// journey's folder. A journey groups an ordered itinerary of ports (and days at
    /// sea) with links out to the trip albums visited along the way. Unlike an
    /// album it has no single location; it is drawn on the map as a route through
    /// its ports.
    /// </summary>
    public class JourneyMetaData
    {
        /// <summary>
        /// Gets or sets the journey's kind (cruise / trek / road trip). Written as its
        /// name (e.g. <c>"Trek"</c>) for a readable, hand-editable file; the converter
        /// also reads the numeric form, and an absent value (older metadata) falls back
        /// to the enum default (<see cref="JourneyKind.Cruise"/>) — so existing journeys
        /// keep working with no migration.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public JourneyKind Kind { get; set; }

        /// <summary>Gets or sets the journey's display name.</summary>
        public string DisplayName { get; set; }

        /// <summary>Gets or sets the journey's free-text description / notes.</summary>
        public string Description { get; set; }

        /// <summary>Gets or sets the date the journey departed.</summary>
        public DateTime StartDate { get; set; }

        /// <summary>Gets or sets the date the journey returned.</summary>
        public DateTime EndDate { get; set; }

        /// <summary>
        /// Gets or sets the colour the journey's route is drawn in on the home map (a
        /// CSS hex colour, e.g. <c>#0e6e78</c>), so journeys that share ports can be
        /// told apart. Absent from older metadata, where it deserializes to
        /// <c>null</c> and falls back to the default route colour.
        /// </summary>
        public string RouteColor { get; set; }

        /// <summary>
        /// Gets or sets the people who were on the journey (free-text names, as on an
        /// album). Absent from older metadata, where it deserializes to <c>null</c>
        /// and is treated as an empty list.
        /// </summary>
        public List<string> People { get; set; }

        /// <summary>
        /// Gets or sets the journey's itinerary, in order. Each entry is a port call
        /// or a day at sea. Absent from older metadata, where it deserializes to
        /// <c>null</c> and is treated as an empty list.
        /// </summary>
        public List<JourneyStop> Stops { get; set; }
    }
}