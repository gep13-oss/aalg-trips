using System;
using System.Collections.Generic;

namespace AalgTrips.Models
{
    /// <summary>
    /// A single day on a cruise's itinerary: either a port call or a day at sea. A
    /// stop with coordinates is a vertex on the route drawn on the home map; an
    /// at-sea ("Cruising") stop has none and appears in the itinerary only. Each
    /// stop can link the trip albums visited while docked there (the map draws a
    /// dotted connector out to each), and — from a later phase — carry its own
    /// photos for that day.
    /// </summary>
    public class CruiseStop
    {
        /// <summary>
        /// Gets or sets the stop's stable key — a filesystem-safe id, assigned once
        /// when the stop is first saved and preserved through every later edit (the
        /// itinerary editor round-trips it in a hidden field). It is the folder a
        /// stop's photos live under (<c>cruises/{slug}/{Key}/</c>), so it must not
        /// change when the stop is reordered, renamed or re-dated. Absent from older
        /// metadata, where it deserializes to <c>null</c> until the next edit assigns
        /// one.
        /// </summary>
        public string Key { get; set; }

        /// <summary>Gets or sets the date of this stop.</summary>
        public DateTime Date { get; set; }

        /// <summary>Gets or sets the stop's name, e.g. <c>Santorini</c> or <c>Cruising</c>.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this stop is a day at sea rather
        /// than a port call. An at-sea stop has no coordinates and is not drawn as a
        /// vertex on the route.
        /// </summary>
        public bool AtSea { get; set; }

        /// <summary>
        /// Gets or sets the arrival time shown in the itinerary (e.g. <c>13:00</c>),
        /// or <c>null</c> when there is none (an embarkation day, or a day at sea).
        /// </summary>
        public string Arrive { get; set; }

        /// <summary>
        /// Gets or sets the departure time shown in the itinerary (e.g. <c>18:00</c>),
        /// or <c>null</c> when there is none (the final day, or a day at sea).
        /// </summary>
        public string Depart { get; set; }

        /// <summary>
        /// Gets or sets the stop's latitude. <c>null</c> for a day at sea; when set
        /// (together with <see cref="Longitude"/>) the stop is a vertex on the
        /// cruise's map route.
        /// </summary>
        public double? Latitude { get; set; }

        /// <summary>Gets or sets the stop's longitude. <c>null</c> for a day at sea.</summary>
        public double? Longitude { get; set; }

        /// <summary>
        /// Gets or sets the ids (slugs) of the trip albums visited from this stop —
        /// the "links to other trips". The map draws a dotted connector from the
        /// port to each linked trip's own location. Absent from older metadata,
        /// where it deserializes to <c>null</c> and is treated as empty.
        /// </summary>
        public List<string> Trips { get; set; }
    }
}