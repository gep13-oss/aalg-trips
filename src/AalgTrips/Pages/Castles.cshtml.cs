using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AalgTrips.Models;
using Microsoft.AspNetCore.Mvc;

namespace AalgTrips.Pages
{
    /// <summary>
    /// The "Castle Bingo" page: every UK castle ordered by distance from Ellon,
    /// ticked off against the family's trips. Viewing is public (behind the site
    /// login); an admin can mark a castle visited or unmark it (the tick is stored
    /// independently of albums), and can spin up a new album for a castle pre-filled
    /// with its details. A castle also reads as visited when a castle-flagged album
    /// sits on it, and any nearby album is linked for its photos.
    /// </summary>
    public class CastlesModel : AdminHandlerPageModel
    {
        // A trip album is treated as sitting on a castle when it is within this many
        // miles of it. Album coordinates are entered by hand near the castle, so a
        // tight radius links the right one.
        private const double AlbumMatchMiles = 1.0;

        // The nations, in the order their filter chips appear.
        private static readonly string[] NationOrder = { "Scotland", "England", "Wales", "Northern Ireland" };

        private readonly CastleCollection _castles;
        private readonly AlbumCollection _albums;
        private readonly VisitedCastles _visited;

        private readonly Dictionary<string, List<Album>> _albumsByCastle = new Dictionary<string, List<Album>>();
        private readonly HashSet<string> _albumVisited = new HashSet<string>(StringComparer.Ordinal);

        public CastlesModel(CastleCollection castles, AlbumCollection albums, VisitedCastles visited)
        {
            _castles = castles;
            _albums = albums;
            _visited = visited;
        }

        /// <summary>Gets every castle, ordered nearest to Ellon first.</summary>
        public IReadOnlyList<Castle> Castles => _castles.Castles;

        /// <summary>Gets the nations present in the catalogue, in filter-chip order.</summary>
        public IReadOnlyList<string> Nations { get; private set; } = new List<string>();

        /// <summary>Gets the number of visitable castles (the page's default view).</summary>
        public int VisitableTotal { get; private set; }

        /// <summary>Gets how many of the visitable castles have been visited.</summary>
        public int VisitableVisited { get; private set; }

        public void OnGet()
        {
            BuildAlbumLinks();

            Nations = NationOrder
                .Where(n => Castles.Any(c => string.Equals(c.Nation, n, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var visitable = Castles.Where(c => c.IsVisitable).ToList();
            VisitableTotal = visitable.Count;
            VisitableVisited = visitable.Count(IsVisited);
        }

        /// <summary>
        /// Gets a value indicating whether the castle has been visited — either ticked
        /// off explicitly, or carrying a castle-flagged album.
        /// </summary>
        /// <param name="castle">The castle to test.</param>
        /// <returns><c>true</c> when the castle counts as visited.</returns>
        public bool IsVisited(Castle castle)
        {
            return castle?.Id != null && (_visited.IsVisited(castle.Id) || _albumVisited.Contains(castle.Id));
        }

        /// <summary>
        /// Gets a value indicating whether the castle was ticked off explicitly (so the
        /// admin UI offers to unmark it). A castle visited only through an album is not
        /// "explicit" — it is unmarked by editing that album.
        /// </summary>
        /// <param name="castle">The castle to test.</param>
        /// <returns><c>true</c> when the castle has an explicit tick.</returns>
        public bool IsExplicitlyMarked(Castle castle)
        {
            return castle?.Id != null && _visited.IsVisited(castle.Id);
        }

        /// <summary>
        /// Gets the albums that sit on the given castle, newest first. Never null.
        /// </summary>
        /// <param name="castle">The castle to look up.</param>
        /// <returns>The nearby albums, or an empty list.</returns>
        public IReadOnlyList<Album> AlbumsFor(Castle castle)
        {
            return castle?.Id != null && _albumsByCastle.TryGetValue(castle.Id, out var albums)
                ? albums
                : (IReadOnlyList<Album>)Array.Empty<Album>();
        }

        /// <summary>Ticks a castle off. Admin only.</summary>
        /// <param name="castleId">The Wikidata id of the castle to mark.</param>
        /// <returns>An OK result, or a challenge/forbid/not-found.</returns>
        public async Task<IActionResult> OnPostMark(string castleId)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (string.IsNullOrWhiteSpace(castleId) || _castles.Castles.All(c => c.Id != castleId))
            {
                return NotFound();
            }

            await _visited.MarkAsync(castleId);
            return new OkResult();
        }

        /// <summary>Removes a castle's explicit tick. Admin only.</summary>
        /// <param name="castleId">The Wikidata id of the castle to unmark.</param>
        /// <returns>An OK result, or a challenge/forbid/bad-request.</returns>
        public async Task<IActionResult> OnPostUnmark(string castleId)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (string.IsNullOrWhiteSpace(castleId))
            {
                return BadRequest();
            }

            await _visited.UnmarkAsync(castleId);
            return new OkResult();
        }

        // Links each album to its nearest castle within the match radius, so a castle
        // shows the trip(s) sitting on it, and remembers which castles carry a
        // castle-flagged album (those read as visited without an explicit tick).
        private void BuildAlbumLinks()
        {
            foreach (var album in _albums.Albums)
            {
                Castle nearest = null;
                double best = double.MaxValue;

                foreach (var castle in _castles.Castles)
                {
                    double miles = GeoDistance.Miles(album.Latitude, album.Longitude, castle.Latitude, castle.Longitude);

                    if (miles < best)
                    {
                        best = miles;
                        nearest = castle;
                    }
                }

                if (nearest == null || best > AlbumMatchMiles)
                {
                    continue;
                }

                if (!_albumsByCastle.TryGetValue(nearest.Id, out var list))
                {
                    list = new List<Album>();
                    _albumsByCastle[nearest.Id] = list;
                }

                list.Add(album);

                if (album.CastleVisited)
                {
                    _albumVisited.Add(nearest.Id);
                }
            }

            // Newest trip first, matching how albums are shown elsewhere.
            foreach (var list in _albumsByCastle.Values)
            {
                list.Sort((a, b) => b.Visited.CompareTo(a.Visited));
            }
        }
    }
}