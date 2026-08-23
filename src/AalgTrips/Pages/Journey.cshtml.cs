using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AalgTrips.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AalgTrips.Pages
{
    /// <summary>
    /// The journey detail page and its admin CRUD handlers, mirroring
    /// <see cref="AlbumsModel"/>: viewing a journey is public (behind the site-wide
    /// login), while create / edit / rename / delete are admin-only and each guards
    /// itself with <see cref="AdminHandlerPageModel.RequireAdmin"/>. A journey's
    /// itinerary is posted as an ordered list of <see cref="JourneyStop"/> rows; the
    /// handlers normalise those (dropping blank rows, clearing coordinates on a day
    /// at sea) before the metadata is stored.
    /// </summary>
    public class JourneysModel : AdminHandlerPageModel
    {
        // A route file is a small hand-off of pre-computed geometry; reject anything
        // that could not plausibly be one before it is read into memory or parsed.
        private const long MaxRouteBytes = 4 * 1024 * 1024;

        private readonly JourneyCollection _cc;
        private readonly AlbumCollection _ac;
        private readonly IPhotoStore _store;
        private readonly ImageProcessor _processor;
        private readonly Dictionary<string, IReadOnlyList<JourneyPhoto>> _stopPhotos =
            new Dictionary<string, IReadOnlyList<JourneyPhoto>>(StringComparer.OrdinalIgnoreCase);

        public JourneysModel(JourneyCollection cc, AlbumCollection ac, IPhotoStore store, ImageProcessor processor)
        {
            _cc = cc;
            _ac = ac;
            _store = store;
            _processor = processor;
        }

        public Journey Journey { get; private set; }

        /// <summary>
        /// Gets a value indicating whether the journey has an uploaded route file, so
        /// the admin UI can offer to remove it. When false the map draws straight
        /// lines between the journey's ports.
        /// </summary>
        public bool HasRoute { get; private set; }

        /// <summary>
        /// Gets the trip albums linked from the journey's stops, in first-seen
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
            Journey = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (Journey == null)
            {
                return NotFound();
            }

            LinkedTrips = ResolveLinkedTrips(Journey);
            LoadStopPhotos(Journey);
            HasRoute = _store.TryReadJourneyRoute(Journey.Id) != null;

            return Page();
        }

        /// <summary>
        /// Gets the photos saved against a stop, in file order. Never null; a stop
        /// with no key yet (older metadata) or no photos returns an empty list.
        /// </summary>
        /// <param name="stopKey">The stop's stable key.</param>
        /// <returns>The stop's photos.</returns>
        public IReadOnlyList<JourneyPhoto> StopPhotos(string stopKey)
        {
            if (!string.IsNullOrWhiteSpace(stopKey) && _stopPhotos.TryGetValue(stopKey, out var photos))
            {
                return photos;
            }

            return Array.Empty<JourneyPhoto>();
        }

        public async Task<IActionResult> OnPostUploadStop(string name, string stopKey, ICollection<IFormFile> files)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (!SafePathHelper.IsValidSegment(name) || !SafePathHelper.IsValidSegment(stopKey))
            {
                return BadRequest();
            }

            var journey = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (journey == null)
            {
                return NotFound();
            }

            // The photos are keyed by stop, so the stop must actually belong to this
            // journey — never trust the posted key to name an arbitrary folder.
            if (!journey.Stops.Any(s => stopKey.Equals(s.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            foreach (var file in files.Where(f => PhotoStoreConventions.IsImageFile(f.FileName)))
            {
                string fileName = Path.GetFileName(file.FileName);

                if (_store.JourneyPhotoExists(name, stopKey, fileName))
                {
                    // Keep both when a name collides, tagging the duplicate with the
                    // upload's hash, exactly as the album upload does.
                    fileName = $"{Path.GetFileNameWithoutExtension(fileName)}.{file.GetHashCode()}{Path.GetExtension(fileName)}";
                }

                // Persist the original first, then derive thumbnails from the saved
                // file, so a decode failure never leaves a half-written original.
                using (var uploadStream = file.OpenReadStream())
                {
                    await _store.SaveJourneyPhotoAsync(name, stopKey, fileName, uploadStream);
                }

                IReadOnlyList<GeneratedThumbnail> thumbnails;

                using (var savedImage = _store.OpenJourneyPhoto(name, stopKey, fileName))
                {
                    thumbnails = _processor.CreateThumbnails(savedImage, fileName);
                }

                if (thumbnails.Count == 0)
                {
                    // The bytes were not a decodable image despite the extension; drop
                    // the saved original and skip it rather than 500.
                    await _store.DeleteJourneyPhotoAsync(name, stopKey, fileName);
                    continue;
                }

                foreach (var thumbnail in thumbnails)
                {
                    using var thumbnailStream = new MemoryStream(thumbnail.Content);
                    await _store.SaveJourneyThumbnailAsync(name, stopKey, thumbnail.FileName, thumbnailStream);
                }
            }

            return new RedirectResult($"~/journey/{WebUtility.UrlEncode(name).Replace('+', ' ')}/");
        }

        public async Task<IActionResult> OnPostDeleteStopPhoto(string name, string stopKey, string photo)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (!SafePathHelper.IsValidSegment(name)
                || !SafePathHelper.IsValidSegment(stopKey)
                || !SafePathHelper.IsValidSegment(photo))
            {
                return BadRequest();
            }

            var journey = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (journey == null)
            {
                return NotFound();
            }

            if (!journey.Stops.Any(s => stopKey.Equals(s.Key, StringComparison.OrdinalIgnoreCase)))
            {
                return NotFound();
            }

            await _store.DeleteJourneyPhotoAsync(name, stopKey, photo);

            return new RedirectResult($"~/journey/{name}/");
        }

        public async Task<IActionResult> OnPostUploadRoute(string name, IFormFile file)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (!SafePathHelper.IsValidSegment(name))
            {
                return BadRequest();
            }

            var journey = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (journey == null)
            {
                return NotFound();
            }

            if (file == null || file.Length == 0 || file.Length > MaxRouteBytes)
            {
                return BadRequest();
            }

            string json;

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                json = await reader.ReadToEndAsync();
            }

            // The route is computed offline and uploaded as GeoJSON; a file that is
            // not a usable line of at least two valid coordinates is rejected rather
            // than stored, so the map never draws a broken route.
            if (!GeoJsonRoute.TryParse(json, out var segments))
            {
                return BadRequest();
            }

            await _store.SaveJourneyRouteAsync(name, segments);
            await _cc.WriteJourneysAsync();

            return new RedirectResult($"~/journey/{name}/");
        }

        public async Task<IActionResult> OnPostDeleteRoute(string name)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            if (!SafePathHelper.IsValidSegment(name))
            {
                return BadRequest();
            }

            var journey = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (journey == null)
            {
                return NotFound();
            }

            await _store.DeleteJourneyRouteAsync(name);
            await _cc.WriteJourneysAsync();

            return new RedirectResult($"~/journey/{name}/");
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

            await _store.DeleteJourneyAsync(name);

            _cc.Remove(name);
            await _cc.WriteJourneysAsync();

            return new RedirectResult("~/");
        }

        public async Task<IActionResult> OnPostCreate(string name, string kind, string description, string startDate, string endDate, string routeColor, List<string> people, List<JourneyStop> stops)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            string slug = SlugHelper.GenerateSlug(name);

            // As with albums, an all-punctuation title can slug to an empty string;
            // reject anything that is not a safe single segment before it is used as
            // the journey's storage folder id.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            if (!TryParseDate(startDate, out var start) || !TryParseDate(endDate, out var end))
            {
                return BadRequest();
            }

            // The slug doubles as the journey's folder id. Two titles that slug to the
            // same value would otherwise write over an existing journey, so refuse the
            // create and leave the existing journey untouched (mirrors album create).
            bool alreadyExists = _store.JourneyExists(slug)
                || _cc.Journeys.Any(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                return StatusCode(StatusCodes.Status409Conflict, $"A journey with the name “{name}” already exists. Please choose a different title.");
            }

            var metaData = new JourneyMetaData
            {
                DisplayName = name,
                Kind = NormalizeKind(kind),
                Description = description,
                StartDate = start,
                EndDate = end,
                RouteColor = NormalizeColor(routeColor),
                People = NormalizePeople(people),
                Stops = NormalizeStops(stops),
            };

            await _store.WriteJourneyAsync(slug, metaData);

            _cc.Add(new Journey(slug, metaData));
            await _cc.WriteJourneysAsync();

            return new RedirectResult($"~/journey/{slug}/");
        }

        public async Task<IActionResult> OnPostEdit([FromRoute(Name = "name")] string slug, string name, string kind, string description, string startDate, string endDate, string routeColor, List<string> people, List<JourneyStop> stops)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            // The journey slug is the route value. Bind it explicitly from the route
            // because the edit form also posts a "name" field (the display name),
            // which the default binder would otherwise let win — exactly as
            // AlbumsModel.OnPostEdit does.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            var existing = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                return NotFound();
            }

            if (!TryParseDate(startDate, out var start) || !TryParseDate(endDate, out var end))
            {
                return BadRequest();
            }

            var metaData = new JourneyMetaData
            {
                DisplayName = name,
                Kind = NormalizeKind(kind),
                Description = description,
                StartDate = start,
                EndDate = end,
                RouteColor = NormalizeColor(routeColor),
                People = NormalizePeople(people),
                Stops = NormalizeStops(stops),
            };

            await _store.WriteJourneyAsync(slug, metaData);

            _cc.ReloadJourney(slug);
            await _cc.WriteJourneysAsync();

            return new RedirectResult($"~/journey/{slug}/");
        }

        public async Task<IActionResult> OnPostRename([FromRoute(Name = "name")] string slug, string name)
        {
            if (RequireAdmin() is { } challenge)
            {
                return challenge;
            }

            // The journey slug is the route value; the posted "name" is the new title.
            // Bind the slug from the route so the form's "name" cannot win.
            if (!SafePathHelper.IsValidSegment(slug))
            {
                return BadRequest();
            }

            var existing = _cc.Journeys.FirstOrDefault(c => c.Id.Equals(slug, StringComparison.OrdinalIgnoreCase));

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

            // Refuse to move onto another journey's folder — that would overwrite it.
            if (slugChanges
                && (_store.JourneyExists(newSlug) || _cc.Journeys.Any(c => c.Id.Equals(newSlug, StringComparison.OrdinalIgnoreCase))))
            {
                return StatusCode(StatusCodes.Status409Conflict, $"A journey with the name “{name}” already exists. Please choose a different title.");
            }

            // Preserve everything but the (new) display name; the itinerary, people
            // and dates carry over unchanged.
            var metaData = new JourneyMetaData
            {
                DisplayName = name,
                Kind = existing.Kind,
                Description = existing.Description,
                StartDate = existing.StartDate,
                EndDate = existing.EndDate,
                RouteColor = existing.RouteColor,
                People = existing.People.ToList(),
                Stops = existing.Stops.ToList(),
            };

            if (slugChanges)
            {
                // Move the journey's content to the new id, stamp the new title onto
                // the moved metadata, then swap the catalogue entry over.
                await _store.RenameJourneyAsync(slug, newSlug);
                await _store.WriteJourneyAsync(newSlug, metaData);
                _cc.RenameJourney(slug, newSlug);
            }
            else
            {
                // The title changed but its slug did not (e.g. only capitalisation):
                // this is an in-place metadata edit.
                await _store.WriteJourneyAsync(slug, metaData);
                _cc.ReloadJourney(slug);
            }

            await _cc.WriteJourneysAsync();

            return new RedirectResult($"~/journey/{newSlug}/");
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
        // sea (so it is never a route vertex), tidies each row's trip slugs, and
        // assigns each stop a stable key (generated when missing, kept when the
        // editor round-tripped one). Row order is preserved — the itinerary is the
        // ordered spine.
        private static List<JourneyStop> NormalizeStops(List<JourneyStop> stops)
        {
            if (stops == null)
            {
                return new List<JourneyStop>();
            }

            var result = new List<JourneyStop>();

            // The key is a folder id, so it must be unique within the journey; a
            // duplicate (a copied row, or a tampered field) is regenerated.
            var usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var stop in stops)
            {
                if (stop == null || string.IsNullOrWhiteSpace(stop.Name))
                {
                    continue;
                }

                result.Add(new JourneyStop
                {
                    Key = ResolveStopKey(stop.Key, stop.Name, usedKeys),
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

            // A journey itinerary is chronological, so order the stops by date (a
            // stable sort keeps the entered order for any that share a date). This is
            // also the order the map route is drawn in, so a stop added out of order
            // still lands in the right place.
            return result.OrderBy(s => s.Date).ToList();
        }

        // Accepts a posted journey kind only if it names a defined JourneyKind;
        // anything else (missing, tampered, or an unknown value) falls back to Cruise,
        // matching how absent metadata deserializes.
        private static JourneyKind NormalizeKind(string kind)
        {
            return Enum.TryParse<JourneyKind>(kind, ignoreCase: true, out var parsed)
                && Enum.IsDefined(typeof(JourneyKind), parsed)
                ? parsed
                : JourneyKind.Cruise;
        }

        // Accepts a posted route colour only if it is a valid #rgb / #rrggbb hex
        // value (the native colour input always posts one); anything else falls back
        // to the default so a bad value can never reach the map's stroke.
        private static string NormalizeColor(string routeColor)
        {
            string value = routeColor?.Trim();

            if (!string.IsNullOrEmpty(value) && Regex.IsMatch(value, "^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$"))
            {
                return value.ToLowerInvariant();
            }

            return Journey.DefaultRouteColor;
        }

        // Keeps a valid, not-yet-used posted key; otherwise generates a fresh one
        // from the stop name plus a short unique suffix. A key drives a folder path,
        // so a tampered or empty value must never be trusted verbatim.
        private static string ResolveStopKey(string providedKey, string name, HashSet<string> usedKeys)
        {
            string key = providedKey?.Trim();

            if (string.IsNullOrEmpty(key) || !SafePathHelper.IsValidSegment(key) || !usedKeys.Add(key))
            {
                do
                {
                    key = GenerateStopKey(name);
                }
                while (!usedKeys.Add(key));
            }

            return key;
        }

        private static string GenerateStopKey(string name)
        {
            string baseSlug = SlugHelper.GenerateSlug(name);
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);

            return string.IsNullOrEmpty(baseSlug) ? $"stop-{suffix}" : $"{baseSlug}-{suffix}";
        }

        private static string BlankToNull(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private void LoadStopPhotos(Journey journey)
        {
            foreach (var stop in journey.Stops)
            {
                if (string.IsNullOrWhiteSpace(stop.Key) || !SafePathHelper.IsValidSegment(stop.Key))
                {
                    continue;
                }

                _stopPhotos[stop.Key] = _store.ListJourneyPhotoFileNames(journey.Id, stop.Key)
                    .Select(fileName => new JourneyPhoto(_store, journey.Id, stop.Key, fileName))
                    .ToList();
            }
        }

        private IReadOnlyList<Album> ResolveLinkedTrips(Journey journey)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<Album>();

            foreach (var stop in journey.Stops)
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