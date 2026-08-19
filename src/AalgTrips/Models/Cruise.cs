using System;
using System.Collections.Generic;
using System.Linq;

namespace AalgTrips.Models
{
    /// <summary>
    /// A cruise projected from its <see cref="CruiseMetaData"/> for display: a
    /// read-only view with never-null collections and the helpers the pages and the
    /// map need. It mirrors <see cref="Album"/> in spirit, but a cruise has no
    /// single location — it is drawn as a route through its ports — and its own
    /// per-day photos are added in a later phase.
    /// </summary>
    public class Cruise
    {
        /// <summary>The route colour used when a cruise has not chosen one.</summary>
        public const string DefaultRouteColor = "#0e6e78";

        public Cruise(string id, CruiseMetaData metaData)
        {
            Id = id;
            Stops = metaData?.Stops ?? new List<CruiseStop>();
            People = metaData?.People ?? new List<string>();
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
        /// Gets the colour the cruise's route is drawn in on the map. Never null or
        /// blank — a cruise with no chosen colour uses <see cref="DefaultRouteColor"/>.
        /// </summary>
        public string RouteColor { get; }

        /// <summary>
        /// Gets the people who were on the cruise (free-text names). Never null; a
        /// cruise with no recorded people exposes an empty list.
        /// </summary>
        public IReadOnlyList<string> People { get; }

        /// <summary>
        /// Gets the cruise's itinerary in order. Never null; each entry is a port
        /// call or a day at sea.
        /// </summary>
        public IReadOnlyList<CruiseStop> Stops { get; }

        /// <summary>
        /// Gets the stops that have coordinates, in itinerary order — the vertices
        /// of the route drawn on the map. Days at sea (which carry no coordinates)
        /// are excluded.
        /// </summary>
        public IReadOnlyList<CruiseStop> Ports =>
            Stops.Where(s => s.Latitude.HasValue && s.Longitude.HasValue).ToList();

        public string UrlName => Id.Replace(" ", "%20").ToLowerInvariant();

        public string Link => $"/cruise/{UrlName}/";
    }
}