namespace AalgTrips.Models
{
    /// <summary>
    /// The kind of a <see cref="Journey"/>. It drives the journey's wording (a
    /// cruise's "ports" vs a trek's "stops", "at sea" vs "travel day"), how the home
    /// page groups journeys, and the default styling of its map route. <see
    /// cref="Cruise"/> is first (enum value 0) so metadata written before journeys had
    /// a kind reads back as a cruise, preserving existing content with no migration.
    /// </summary>
    public enum JourneyKind
    {
        /// <summary>A sea voyage calling at ports.</summary>
        Cruise,

        /// <summary>A walking/hiking trek between stops.</summary>
        Trek,

        /// <summary>A road trip driven between stops.</summary>
        RoadTrip,
    }
}