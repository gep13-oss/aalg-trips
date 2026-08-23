using System.Collections.Generic;
using AalgTrips.Models;

namespace AalgTrips.Pages
{
    /// <summary>
    /// The model for a single <c>_ItineraryRow</c> partial. <see cref="Index"/> is a
    /// string rather than an int so the same partial renders both a concrete row
    /// (<c>"0"</c>, <c>"1"</c>, …) and the hidden <c>&lt;template&gt;</c> row whose
    /// field names carry the <c>__INDEX__</c> placeholder that <c>journey-admin.js</c>
    /// rewrites when the row is cloned into the form.
    /// </summary>
    public class ItineraryRowModel
    {
        /// <summary>Gets or sets the row's binding index (or the <c>__INDEX__</c> placeholder).</summary>
        public string Index { get; set; }

        /// <summary>Gets or sets the stop to pre-fill the row with, or <c>null</c> for a blank row.</summary>
        public JourneyStop Stop { get; set; }

        /// <summary>Gets or sets the albums offered as trip-link options on this stop.</summary>
        public IReadOnlyList<Album> Albums { get; set; }
    }
}