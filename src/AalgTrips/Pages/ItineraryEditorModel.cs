using System.Collections.Generic;
using AalgTrips.Models;

namespace AalgTrips.Pages
{
    /// <summary>
    /// The model for the <c>_ItineraryEditor</c> partial: the journey's existing
    /// stops (empty when adding a new journey) and the album catalogue offered as
    /// the per-stop trip-link options.
    /// </summary>
    public class ItineraryEditorModel
    {
        /// <summary>Gets or sets the stops to render as editor rows, in order.</summary>
        public IReadOnlyList<JourneyStop> Stops { get; set; }

        /// <summary>Gets or sets the albums offered as trip-link options on each stop.</summary>
        public IReadOnlyList<Album> Albums { get; set; }
    }
}