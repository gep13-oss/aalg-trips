namespace AalgTrips.Models
{
    /// <summary>
    /// The kind-specific wording for a <see cref="Journey"/>, so the detail page,
    /// cards, itinerary editor, home-page sections and album backlink all read
    /// naturally for a cruise, a trek or a road trip from a single source. A cruise
    /// calls at "ports" and spends days "at sea"; a trek or road trip has "stops" and
    /// "travel days".
    /// </summary>
    public sealed class JourneyVocabulary
    {
        private JourneyVocabulary(
            string noun,
            string nounPlural,
            string title,
            string titlePlural,
            string stopNoun,
            string stopNounPlural,
            string travelDayLabel)
        {
            Noun = noun;
            NounPlural = nounPlural;
            Title = title;
            TitlePlural = titlePlural;
            StopNoun = stopNoun;
            StopNounPlural = stopNounPlural;
            TravelDayLabel = travelDayLabel;
        }

        /// <summary>Gets the lower-case singular noun for the journey (e.g. <c>cruise</c>).</summary>
        public string Noun { get; }

        /// <summary>Gets the lower-case plural noun for the journey (e.g. <c>cruises</c>).</summary>
        public string NounPlural { get; }

        /// <summary>Gets the title-case singular noun (e.g. <c>Cruise</c>), for buttons and labels.</summary>
        public string Title { get; }

        /// <summary>Gets the title-case plural noun (e.g. <c>Cruises</c>), for section headings.</summary>
        public string TitlePlural { get; }

        /// <summary>Gets the lower-case singular word for a located stop (<c>port</c> / <c>stop</c>).</summary>
        public string StopNoun { get; }

        /// <summary>Gets the lower-case plural word for located stops (<c>ports</c> / <c>stops</c>).</summary>
        public string StopNounPlural { get; }

        /// <summary>Gets the label for a stop with no location (<c>At sea</c> / <c>Travel day</c>).</summary>
        public string TravelDayLabel { get; }

        /// <summary>
        /// Gets the wording for the given <paramref name="kind"/>.
        /// </summary>
        /// <param name="kind">The journey kind.</param>
        /// <returns>The matching vocabulary; a cruise's wording for the default kind.</returns>
        public static JourneyVocabulary For(JourneyKind kind)
        {
            return kind switch
            {
                JourneyKind.Trek => new JourneyVocabulary("trek", "treks", "Trek", "Treks", "stop", "stops", "Travel day"),
                JourneyKind.RoadTrip => new JourneyVocabulary("road trip", "road trips", "Road trip", "Road trips", "stop", "stops", "Travel day"),
                _ => new JourneyVocabulary("cruise", "cruises", "Cruise", "Cruises", "port", "ports", "At sea"),
            };
        }
    }
}