using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AalgTrips.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AalgTrips.Pages
{
    /// <summary>
    /// The cruise detail page and its admin CRUD handlers, mirroring
    /// <see cref="AlbumsModel"/>: viewing a cruise is public (behind the site-wide
    /// login), while create / edit / rename / delete are admin-only and each guards
    /// itself with <see cref="AdminHandlerPageModel.RequireAdmin"/>. A cruise's
    /// itinerary is posted as an ordered list of <see cref="CruiseStop"/> rows; the
    /// handlers normalise those (dropping blank rows, clearing coordinates on a day
    /// at sea) before the metadata is stored.
    /// </summary>
    public class CruisesModel : AdminHandlerPageModel
    {
        private readonly CruiseCollection _cc;
        private readonly AlbumCollection _ac;
        private readonly IPhotoStore _store;

        public CruisesModel(CruiseCollection cc, AlbumCollection ac, IPhotoStore store)
        {
            _cc = cc;
            _ac = ac;
            _store = store;
        }

        public Cruise Cruise { get; private set; }

        /// <summary>
        /// Gets the trip albums linked from the cruise's stops, in first-seen
        /// itinerary order and de-duplicated, resolved against the album catalogue.
        /// Slugs with no matching album are dropped. Never null.
        /// </summary>
        public IReadOnlyList<Album> LinkedTrips { get; private set; } = new List<Album>();

        /// <summary>
        /// Gets the album catalogue, so the itinerary editor can offer every
        /// existing trip as a link target for a stop.
        /// </summary>
        public IReadOnlyList<Album> Albums => _ac.Albums;

        public IActionResult OnGet(string name)
        {
            Cruise = _cc.Cruises.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (Cruise == null)
            {
                return NotFound();
            }

            LinkedTrips = ResolveLinkedTrips(Cruise);

            return Page();
        }

        public async Task<IActionResult> OnPostDelete(string name)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (!SafePathHelper.IsValidSegment(name))
            {
                return BadRequest();
            }

            await _store.DeleteCruiseAsync(name);

            _cc.Remove(name);
            await _cc.WriteCruisesAsync();

            return new RedirectResult("~/");
        }

        public async Task<IActionResult> OnPostCreate(string name, string description, string startDate, string endDate, List<string> people, List<CruiseStop> stops)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            string slug = SlugHelper.GenerateSlug(name);

            // As with albums, an all-punctuation title can slug to an empty string;
            // reject anything that is not a safe single segment before it is used as
            // the cruise's storage folder id.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            if (!TryParseDate(startDate, out var start) || !TryParseDate(endDate, out var end))
            {
                return BadRequest();
            }

            // The slug doubles as the cruise's folder id. Two titles that slug to the
            // same value would otherwise write over an existing cruise, so refuse the
            // create and leave the existing cruise untouched (mirrors album create).
            bool alreadyExists = _store.CruiseExists(slug)
                || _cc.Cruises.Any(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                return StatusCode(StatusCodes.Status409Conflict, $"A cruise with the name “{name}” already exists. Please choose a different title.");
            }

            var metaData = new CruiseMetaData
            {
                DisplayName = name,
                Description = description,
                StartDate = start,
                EndDate = end,
                People = NormalizePeople(people),
                Stops = NormalizeStops(stops),
            };

            await _store.WriteCruiseAsync(slug, metaData);

            _cc.Add(new Cruise(slug, metaData));
            await _cc.WriteCruisesAsync();

            return new RedirectResult($"~/cruise/{slug}/");
        }

        public async Task<IActionResult> OnPostEdit([FromRoute(Name = "name")] string slug, string name, string description, string startDate, string endDate, List<string> people, List<CruiseStop> stops)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            // The cruise slug is the route value. Bind it explicitly from the route
            // because the edit form also posts a "name" field (the display name),
            // which the default binder would otherwise let win — exactly as
            // AlbumsModel.OnPostEdit does.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            var existing = _cc.Cruises.FirstOrDefault(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                return NotFound();
            }

            if (!TryParseDate(startDate, out var start) || !TryParseDate(endDate, out var end))
            {
                return BadRequest();
            }

            var metaData = new CruiseMetaData
            {
                DisplayName = name,
                Description = description,
                StartDate = start,
                EndDate = end,
                People = NormalizePeople(people),
                Stops = NormalizeStops(stops),
            };

            await _store.WriteCruiseAsync(slug, metaData);

            _cc.ReloadCruise(slug);
            await _cc.WriteCruisesAsync();

            return new RedirectResult($"~/cruise/{slug}/");
        }

        public async Task<IActionResult> OnPostRename([FromRoute(Name = "name")] string slug, string name)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            // The cruise slug is the route value; the posted "name" is the new title.
            // Bind the slug from the route so the form's "name" cannot win.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            var existing = _cc.Cruises.FirstOrDefault(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                return NotFound();
            }

            string newSlug = SlugHelper.GenerateSlug(name);

            if (!SafePathHelper.IsValidSegment(newSlug))
            {
                return BadRequest();
            }

            bool slugChanges = !newSlug.Equals(slug, StringComparison.OrdinalIgnoreCase);

            // Refuse to move onto another cruise's folder — that would overwrite it.
            if (slugChanges
                && (_store.CruiseExists(newSlug) || _cc.Cruises.Any(c => c.Id.Equals(newSlug, StringComparison.OrdinalIgnoreCase))))
            {
                return StatusCode(StatusCodes.Status409Conflict, $"A cruise with the name “{name}” already exists. Please choose a different title.");
            }

            // Preserve everything but the (new) display name; the itinerary, people
            // and dates carry over unchanged.
            var metaData = new CruiseMetaData
            {
                DisplayName = name,
                Description = existing.Description,
                StartDate = existing.StartDate,
                EndDate = existing.EndDate,
                People = existing.People.ToList(),
                Stops = existing.Stops.ToList(),
            };

            if (slugChanges)
            {
                // Move the cruise's content to the new id, stamp the new title onto
                // the moved metadata, then swap the catalogue entry over.
                await _store.RenameCruiseAsync(slug, newSlug);
                await _store.WriteCruiseAsync(newSlug, metaData);
                _cc.RenameCruise(slug, newSlug);
            }
            else
            {
                // The title changed but its slug did not (e.g. only capitalisation):
                // this is an in-place metadata edit.
                await _store.WriteCruiseAsync(slug, metaData);
                _cc.ReloadCruise(slug);
            }

            await _cc.WriteCruisesAsync();

            return new RedirectResult($"~/cruise/{newSlug}/");
        }

        /// <summary>
        /// Finds the album a stop's trip slug refers to, or <c>null</c> when no such
        /// album exists (a linked trip that has since been deleted or renamed).
        /// </summary>
        /// <param name="slug">The trip album slug recorded on a stop.</param>
        /// <returns>The matching album, or <c>null</c>.</returns>
        public Album FindTrip(string slug)
        {
            return _ac.Albums.FirstOrDefault(a => a.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));
        }

        // Parses a date posted from an <input type="date"> (an ISO yyyy-MM-dd value)
        // in the invariant culture, so the result never depends on the server locale.
        private static bool TryParseDate(string value, out DateTime date)
        {
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        // Cleans the posted people list: drops null/blank entries and trims the rest,
        // returning an empty (never null) list, exactly as AlbumsModel does.
        private static List<string> NormalizePeople(List<string> people)
        {
            return people?
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToList() ?? new List<string>();
        }

        // Cleans the posted itinerary: drops rows with no name, trims the names,
        // blanks empty arrive/depart times to null, clears coordinates on a day at
        // sea (so it is never a route vertex), and tidies each row's trip slugs.
        // Row order is preserved — the itinerary is the ordered spine.
        private static List<CruiseStop> NormalizeStops(List<CruiseStop> stops)
        {
            if (stops == null)
            {
                return new List<CruiseStop>();
            }

            var result = new List<CruiseStop>();

            foreach (var stop in stops)
            {
                if (stop == null || string.IsNullOrWhiteSpace(stop.Name))
                {
                    continue;
                }

                result.Add(new CruiseStop
                {
                    Date = stop.Date,
                    Name = stop.Name.Trim(),
                    AtSea = stop.AtSea,
                    Arrive = BlankToNull(stop.Arrive),
                    Depart = BlankToNull(stop.Depart),
                    Latitude = stop.AtSea ? null : stop.Latitude,
                    Longitude = stop.AtSea ? null : stop.Longitude,
                    Trips = (stop.Trips ?? new List<string>())
                        .Where(t => !string.IsNullOrWhiteSpace(t))
                        .Select(t => t.Trim())
                        .ToList(),
                });
            }

            return result;
        }

        private static string BlankToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private IReadOnlyList<Album> ResolveLinkedTrips(Cruise cruise)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Album>();

            foreach (var stop in cruise.Stops)
            {
                foreach (var slug in stop.Trips ?? Enumerable.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(slug) || !seen.Add(slug))
                    {
                        continue;
                    }

                    var album = _ac.Albums.FirstOrDefault(a => a.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

                    if (album != null)
                    {
                        result.Add(album);
                    }
                }
            }

            return result;
        }
    }
}