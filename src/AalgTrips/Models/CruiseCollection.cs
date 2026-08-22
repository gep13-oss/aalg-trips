using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// The in-memory catalogue of cruises, registered as a singleton and shared
    /// across every request. Like <see cref="AlbumCollection"/> it mutates
    /// copy-on-write under a lock — each mutation builds a new list and swaps the
    /// <see cref="Cruises"/> reference — so a public reader only ever enumerates a
    /// fully-published list and never observes a half-applied change. Cruise
    /// content is read from and written to the same <see cref="IPhotoStore"/> the
    /// albums use, under a separate <c>cruises</c> area that is kept out of the
    /// album catalogue.
    /// </summary>
    public class CruiseCollection
    {
        private readonly IPhotoStore _store;
        private readonly object _sync = new object();

        public CruiseCollection(IPhotoStore store)
        {
            _store = store;
            Cruises = new List<Cruise>();

            Initialize();
        }

        public List<Cruise> Cruises { get; private set; }

        /// <summary>
        /// Gets the public URL the map's cruise-route file is served from, for the
        /// home page to hand to the client-side map script.
        /// </summary>
        /// <returns>The cruise-route file URL.</returns>
        public string CruisesUrl()
        {
            return _store.CruisesUrl();
        }

        /// <summary>
        /// Adds a newly created cruise and re-sorts the collection.
        /// </summary>
        /// <param name="cruise">The cruise to add.</param>
        public void Add(Cruise cruise)
        {
            lock (_sync)
            {
                Cruises = InDisplayOrder(new List<Cruise>(Cruises) { cruise });
            }
        }

        /// <summary>
        /// Removes the cruise whose <see cref="Cruise.Id"/> matches
        /// <paramref name="id"/>, if it is present.
        /// </summary>
        /// <param name="id">The id (folder name) of the cruise to remove.</param>
        public void Remove(string id)
        {
            lock (_sync)
            {
                Cruises = Cruises
                    .Where(c => !c.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }

        /// <summary>
        /// Reloads a single cruise from the store and swaps the fresh instance into
        /// the collection, replacing any existing cruise with the same id. This is
        /// how an edit that rewrote the cruise's metadata is reflected.
        /// </summary>
        /// <param name="cruiseId">The id of the cruise to reload.</param>
        public void ReloadCruise(string cruiseId)
        {
            var reloaded = GetCruise(cruiseId);

            lock (_sync)
            {
                var updated = Cruises
                    .Where(c => !c.Id.Equals(reloaded.Id, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Cruises = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Reflects a completed store rename in the catalogue: the cruise that was
        /// under <paramref name="oldId"/> is dropped and the moved cruise is loaded
        /// fresh under <paramref name="newId"/>, both swapped in a single
        /// publication. The store move must already have happened.
        /// </summary>
        /// <param name="oldId">The cruise's previous id.</param>
        /// <param name="newId">The cruise's new id.</param>
        public void RenameCruise(string oldId, string newId)
        {
            var reloaded = GetCruise(newId);

            lock (_sync)
            {
                var updated = Cruises
                    .Where(c => !c.Id.Equals(oldId, StringComparison.OrdinalIgnoreCase)
                        && !c.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                updated.Add(reloaded);
                Cruises = InDisplayOrder(updated);
            }
        }

        /// <summary>
        /// Rewrites the cruise-route file from the current cruise set so the map
        /// stays in step after a create, edit or delete. The routes are snapshotted
        /// under the lock; the store write happens outside it.
        /// </summary>
        /// <returns>A task that completes when the cruise-route file has been written.</returns>
        public async Task WriteCruisesAsync()
        {
            List<Cruise> snapshot;

            lock (_sync)
            {
                snapshot = Cruises.ToList();
            }

            // Reading each cruise's uploaded route file is I/O, so map the snapshot
            // outside the lock rather than holding it across store reads.
            var routes = snapshot.Select(ToRoute).ToList();

            await _store.WriteCruisesAsync(routes);
        }

        // Projects a cruise onto its map route: only the stops that have
        // coordinates become ports (days at sea are skipped), preserving itinerary
        // order, and each port carries its linked trip slugs for the connectors. A
        // cruise with an uploaded route file also carries its pre-computed geometry,
        // which the client draws the line along instead of straight port-to-port hops.
        private CruiseRoute ToRoute(Cruise cruise)
        {
            var geometry = _store.TryReadCruiseRoute(cruise.Id);

            return new CruiseRoute
            {
                Slug = cruise.Id,
                Name = cruise.DisplayName,
                Color = cruise.RouteColor,
                Ports = cruise.Ports
                    .Select(s => new CruisePort
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
            var cruises = _store.ListCruiseIds()
                .Select(GetCruise)
                .ToList();

            Cruises = InDisplayOrder(cruises);
        }

        // Cruises are shown newest departure first, with the id as a stable
        // tie-breaker so cruises sharing a start date keep a deterministic order.
        private static List<Cruise> InDisplayOrder(IEnumerable<Cruise> cruises)
        {
            return cruises
                .OrderByDescending(c => c.StartDate)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private Cruise GetCruise(string cruiseId)
        {
            var metadata = _store.TryReadCruise(cruiseId);
            return new Cruise(cruiseId, metadata);
        }
    }
}