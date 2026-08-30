using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AalgTrips.Models
{
    /// <summary>
    /// The set of castles explicitly ticked off on the "Castle Bingo" page, held as a
    /// singleton and backed by the <see cref="IPhotoStore"/> so it survives a redeploy
    /// (and works against Azure Blob in production). Like the album and journey
    /// catalogues it mutates copy-on-write under a lock: each change builds a new set
    /// and swaps the reference, so a reader only ever sees a fully-published set.
    /// A castle can also read as visited via a nearby castle-flagged album (see the
    /// castle page); this store is the manual, album-independent tick.
    /// </summary>
    public class VisitedCastles
    {
        private readonly IPhotoStore _store;
        private readonly object _sync = new object();

        private HashSet<string> _ids;

        public VisitedCastles(IPhotoStore store)
        {
            _store = store;
            _ids = new HashSet<string>(_store.ReadVisitedCastles(), StringComparer.Ordinal);
        }

        /// <summary>Gets the number of castles explicitly marked as visited.</summary>
        public int Count => _ids.Count;

        /// <summary>
        /// Determines whether a castle has been explicitly marked as visited.
        /// </summary>
        /// <param name="castleId">The castle id to test.</param>
        /// <returns><c>true</c> when the castle has been ticked off.</returns>
        public bool IsVisited(string castleId)
        {
            return castleId != null && _ids.Contains(castleId);
        }

        /// <summary>
        /// Marks a castle as visited and persists the set. A no-op (no write) when the
        /// castle is already marked.
        /// </summary>
        /// <param name="castleId">The castle id to mark.</param>
        /// <returns>A task that completes when the set has been stored.</returns>
        public async Task MarkAsync(string castleId)
        {
            List<string> snapshot;

            lock (_sync)
            {
                if (_ids.Contains(castleId))
                {
                    return;
                }

                var updated = new HashSet<string>(_ids, StringComparer.Ordinal) { castleId };
                _ids = updated;
                snapshot = updated.ToList();
            }

            await _store.WriteVisitedCastlesAsync(snapshot);
        }

        /// <summary>
        /// Removes a castle's explicit visited mark and persists the set. A no-op (no
        /// write) when the castle was not marked.
        /// </summary>
        /// <param name="castleId">The castle id to unmark.</param>
        /// <returns>A task that completes when the set has been stored.</returns>
        public async Task UnmarkAsync(string castleId)
        {
            List<string> snapshot;

            lock (_sync)
            {
                if (!_ids.Contains(castleId))
                {
                    return;
                }

                var updated = new HashSet<string>(_ids, StringComparer.Ordinal);
                updated.Remove(castleId);
                _ids = updated;
                snapshot = updated.ToList();
            }

            await _store.WriteVisitedCastlesAsync(snapshot);
        }
    }
}