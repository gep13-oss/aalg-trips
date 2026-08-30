using System.Globalization;
using System.Text.Json.Serialization;

namespace AalgTrips.Models
{
    /// <summary>
    /// A single UK castle from the generated catalogue (see tools/generate-castles.py).
    /// The JSON is a flat record sourced from Wikidata; the computed members below
    /// (distance from home, whether it is worth listing, a display slug) are derived
    /// from it at runtime and never serialized.
    /// </summary>
    public class Castle
    {
        /// <summary>Latitude of Ellon, Aberdeenshire — home; the list is ordered by distance from here.</summary>
        public const double EllonLatitude = 57.3626;

        /// <summary>Longitude of Ellon, Aberdeenshire — home; the list is ordered by distance from here.</summary>
        public const double EllonLongitude = -2.0817;

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("lat")]
        public double Latitude { get; set; }

        [JsonPropertyName("lon")]
        public double Longitude { get; set; }

        [JsonPropertyName("nation")]
        public string Nation { get; set; }

        [JsonPropertyName("admin")]
        public string Admin { get; set; }

        /// <summary>
        /// Gets or sets the short operator badge (for example "NTS", "HES", "Cadw"),
        /// or the operator's name when it is not a membership body. Null when the
        /// castle has no recorded operator.
        /// </summary>
        [JsonPropertyName("operator")]
        public string Operator { get; set; }

        [JsonPropertyName("access")]
        public AccessTier Access { get; set; }

        [JsonPropertyName("website")]
        public string Website { get; set; }

        [JsonPropertyName("heritage")]
        public bool Heritage { get; set; }

        /// <summary>Gets the great-circle distance from Ellon, in miles.</summary>
        [JsonIgnore]
        public double DistanceMiles => GeoDistance.Miles(EllonLatitude, EllonLongitude, Latitude, Longitude);

        /// <summary>
        /// Gets a value indicating whether the castle is worth listing by default: it
        /// has a known operator, an official website, or a heritage designation. The
        /// page opens on these and hides the bare ruins and earthworks behind a
        /// "show everything" toggle.
        /// </summary>
        [JsonIgnore]
        public bool IsVisitable => !string.IsNullOrEmpty(Operator) || !string.IsNullOrEmpty(Website) || Heritage;

        /// <summary>Gets the distance rendered for the card, e.g. "5 mi" or "122 mi".</summary>
        [JsonIgnore]
        public string DistanceLabel => string.Format(CultureInfo.InvariantCulture, "{0:0} mi", DistanceMiles);
    }
}