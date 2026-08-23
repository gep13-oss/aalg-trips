using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// The in-memory catalogue of journeys, registered as a singleton and shared
    /// across every request. Like <see cref="AlbumCollection"/> it mutates
    /// copy-on-write under a lock — each mutation builds a new list and swaps the
    /// <see cref="Journeys"/> reference — so a public reader only ever enumerates a
    /// fully-published list and never observes a half-applied change. Journey
    /// content is read from and written to the same <see cref="IPhotoStore"/> the
    /// albums use, under a separate <c>journeys</c> area that is kept out of the
    /// album catalogue.
    /// </summary>
    public class JourneyCollection
    {
        private readonly IPhotoStore _store;
        private readonly object _sync = new object();

        public JourneyCollection(IPhotoStore store)
        {
            _store = store;
            Journeys = new List<Journey>();

            Initialize();
        }

        public List<Journey> Journeys { get; private set; }

        /// <summary>
        /// Gets the public URL the map's journey-route file is served from, for the
        /// home page to hand to the client-side map script.
        /// </summary>
        /// <returns>The journey-route file URL.</returns>
        public string JourneysUrl()
        {
            return _store.JourneysUrl();
        }

        /// <summary>
        /// Adds a newly created journey and re-sorts the collection.
        /// </summary>
        /// <param name="journey">The journey to add.</param>
        public void Add(Journey journey)
        {
            lock (_sync)
            {
                Journeys = InDisplayOrder(new List<Journey>(Journeys) { journey });
            }
        }

        /// <summary>
        /// Removes the journey whose <see cref="Journey.Id"/> matches
        /// <paramref name="id"/>, if it is present.
        /// </summary>
        /// <param name="id">The id (folder name) of the journey to remove.</param>
        public void Remove(string id)
        {
            lock (_sync)
            {
                Journeys = Journeys
                    .Where(c => !c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>
        /// Reloads a single journey from the store and swaps the fresh instance into
        /// the collection, replacing any existing journey with the same id. This is
        /// how an edit that rewrote the journey's metadata is reflected.
        /// </summary>
        /// <param name="journeyId">The id of the journey to reload.</param>
        public void ReloadJourney(string journeyId)
        {
            var reloaded = GetJourney(journeyId);

            lock (_sync)
            {
                var updated = Journeys
                    .Where(c => !c.Id.Equals(reloaded.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Journeys = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Reflects a completed store rename in the catalogue: the journey that was
        /// under <paramref name="oldId"/> is dropped and the moved journey is loaded
        /// fresh under <paramref name="newId"/>, both swapped in a single
        /// publication. The store move must already have happened.
        /// </summary>
        /// <param name="oldId">The journey's previous id.</param>
        /// <param name="newId">The journey's new id.</param>
        public void RenameJourney(string oldId, string newId)
        {
            var reloaded = GetJourney(newId);

            lock (_sync)
            {
                var updated = Journeys
                    .Where(c => !c.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase)
                        && !c.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Journeys = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Rewrites the journey-route file from the current journey set so the map
        /// stays in step after a create, edit or delete. The routes are snapshotted
        /// under the lock; the store write happens outside it.
        /// </summary>
        /// <returns>A task that completes when the journey-route file has been written.</returns>
        public async Task WriteJourneysAsync()
        {
            List<Journey> snapshot;

            lock (_sync)
            {
                snapshot = Journeys.ToList();
            }

            // Reading each journey's uploaded route file is I/O, so map the snapshot
            // outside the lock rather than holding it across store reads.
            var routes = snapshot.Select(ToRoute).ToList();

            await _store.WriteJourneysAsync(routes);
        }

        // Projects a journey onto its map route: only the stops that have coordinates
        // become waypoints (travel days are skipped), preserving itinerary order, and
        // each waypoint carries its linked trip slugs for the connectors. A journey with
        // an uploaded route file also carries its pre-computed geometry, which the client
        // draws the line along instead of straight stop-to-stop hops.
        private JourneyRoute ToRoute(Journey journey)
        {
            var geometry = _store.TryReadJourneyRoute(journey.Id);

            return new JourneyRoute
            {
                Slug = journey.Id,
                Name = journey.DisplayName,
                Kind = journey.Kind,
                Color = journey.RouteColor,
                Waypoints = journey.Waypoints
                    .Select(s => new JourneyWaypoint
                    {
                        Lat = s.Latitude.Value,
                        Long = s.Longitude.Value,
                        Name = s.Name,
                        Date = s.Date.ToString("d MMM yyyy", CultureInfo.InvariantCulture),
                        Arrive = s.Arrive,
                        Depart = s.Depart,
                        Trips = (s.Trips ?? new List<string>()).ToList(),
                    })
                    .ToList(),
                Geometry = geometry?.ToList(),
            };
        }

        private void Initialize()
        {
            var journeys = _store.ListJourneyIds()
                .Select(GetJourney)
                .ToList();

            Journeys = InDisplayOrder(journeys);
        }

        // Journeys are shown newest departure first, with the id as a stable
        // tie-breaker so journeys sharing a start date keep a deterministic order.
        private static List<Journey> InDisplayOrder(IEnumerable<Journey> journeys)
        {
            return journeys
                .OrderByDescending(c => c.StartDate)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Journey GetJourney(string journeyId)
        {
            var metadata = _store.TryReadJourney(journeyId);
            return new Journey(journeyId, metadata);
        }
    }
}