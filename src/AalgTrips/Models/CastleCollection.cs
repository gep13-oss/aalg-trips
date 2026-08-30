using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AalgTrips.Models
{
    /// <summary>
    /// The catalogue of UK castles for the "Castle Bingo" page, loaded once from the
    /// embedded <c>Data/castles.json</c> resource and held as a singleton. Unlike the
    /// album and journey catalogues this is fixed reference data (generated offline
    /// from Wikidata), so it is read-only and never mutated at runtime. The list is
    /// ordered nearest-to-Ellon first, which is the order the page shows.
    /// </summary>
    public class CastleCollection
    {
        // The generated data uses lower-case JSON keys and a string access tier, so a
        // castle deserializes with case-insensitive matching and the enum converter.
        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public CastleCollection()
            : this(LoadEmbedded())
        {
        }

        // Kept internal so unit tests can build a collection from a controlled set
        // rather than the full embedded catalogue.
        internal CastleCollection(IEnumerable<Castle> castles)
        {
            Castles = castles
                .Where(c => c != null && c.Name != null)
                .OrderBy(c => c.DistanceMiles)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Gets the castles, ordered by distance from Ellon (nearest first).</summary>
        public IReadOnlyList<Castle> Castles { get; }

        /// <summary>
        /// Builds a collection from a JSON document in the generated shape. Used by
        /// the tests; the running site loads the embedded resource instead.
        /// </summary>
        /// <param name="json">The castle catalogue as JSON.</param>
        /// <returns>A collection over the parsed castles.</returns>
        public static CastleCollection FromJson(string json)
        {
            var castles = JsonSerializer.Deserialize<List<Castle>>(json, SerializerOptions) ?? new List<Castle>();
            return new CastleCollection(castles);
        }

        private static List<Castle> LoadEmbedded()
        {
            var assembly = typeof(CastleCollection).Assembly;
            const string resource = "AalgTrips.Data.castles.json";

            using var stream = assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded castle catalogue '{resource}' was not found.");
            using var reader = new StreamReader(stream);

            return JsonSerializer.Deserialize<List<Castle>>(reader.ReadToEnd(), SerializerOptions) ?? new List<Castle>();
        }
    }
}