using System;
using System.Collections.Generic;
using System.Linq;

namespace AalgTrips.Models
{
    /// <summary>
    /// A journey projected from its <see cref="JourneyMetaData"/> for display: a
    /// read-only view with never-null collections and the helpers the pages and the
    /// map need. It mirrors <see cref="Album"/> in spirit, but a journey has no single
    /// location — it is an ordered itinerary of stops drawn on the map as a route —
    /// and it holds its own per-day photos. A journey's <see cref="Kind"/> (journey,
    /// trek, road trip) drives its wording via <see cref="Vocabulary"/>.
    /// </summary>
    public class Journey
    {
        /// <summary>The route colour used when a journey has not chosen one.</summary>
        public const string DefaultRouteColor = "#0e6e78";

        public Journey(string id, JourneyMetaData metaData)
        {
            Id = id;
            Stops = metaData?.Stops ?? new List<JourneyStop>();
            People = metaData?.People ?? new List<string>();
            Kind = metaData?.Kind ?? JourneyKind.Cruise;
            RouteColor = string.IsNullOrWhiteSpace(metaData?.RouteColor) ? DefaultRouteColor : metaData.RouteColor;

            if (metaData != null)
            {
                DisplayName = metaData.DisplayName;
                Description = metaData.Description;
                StartDate = metaData.StartDate;
                EndDate = metaData.EndDate;
            }
        }

        public string Id { get; }

        public string DisplayName { get; }

        public string Description { get; }

        public DateTime StartDate { get; }

        public DateTime EndDate { get; }

        /// <summary>
        /// Gets the journey's kind (journey / trek / road trip). Defaults to
        /// <see cref="JourneyKind.Cruise"/> for metadata written before journeys had a
        /// kind, so existing cruises keep working with no migration.
        /// </summary>
        public JourneyKind Kind { get; }

        /// <summary>Gets the kind-specific wording for this journey.</summary>
        public JourneyVocabulary Vocabulary => JourneyVocabulary.For(Kind);

        /// <summary>
        /// Gets the colour the journey's route is drawn in on the map. Never null or
        /// blank — a journey with no chosen colour uses <see cref="DefaultRouteColor"/>.
        /// </summary>
        public string RouteColor { get; }

        /// <summary>
        /// Gets the people who were on the journey (free-text names). Never null; a
        /// journey with no recorded people exposes an empty list.
        /// </summary>
        public IReadOnlyList<string> People { get; }

        /// <summary>
        /// Gets the journey's itinerary in order. Never null; each entry is a located
        /// stop or a travel day (a day at sea, in transit, or resting).
        /// </summary>
        public IReadOnlyList<JourneyStop> Stops { get; }

        /// <summary>
        /// Gets the stops that have coordinates, in itinerary order — the vertices of
        /// the route drawn on the map. Travel days (which carry no coordinates) are
        /// excluded.
        /// </summary>
        public IReadOnlyList<JourneyStop> Waypoints =>
            Stops.Where(s => s.Latitude.HasValue && s.Longitude.HasValue).ToList();

        public string UrlName => Id.Replace(" ", "%20").ToLowerInvariant();

        public string Link => $"/journey/{UrlName}/";
    }
}