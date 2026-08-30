namespace AalgTrips.Models
{
    /// <summary>
    /// How much a visit to a castle costs, so the list can say whether a trip is
    /// free, free to a heritage-body member, or pay-on-the-day. Derived from the
    /// castle's operator when the data is generated (a membership body maps to
    /// <see cref="MembersFree"/>); <see cref="Unknown"/> is first (enum value 0) so a
    /// castle with no operator information reads back as unknown rather than a
    /// misleading price. <see cref="FreeEntry"/> is only ever set by a manual
    /// override (an open ruin the data cannot tell apart from a paid site).
    /// </summary>
    public enum AccessTier
    {
        /// <summary>No access information — likely no public visiting, or simply unrecorded.</summary>
        Unknown,

        /// <summary>Run by a membership body (NTS, HES, National Trust, English Heritage, Cadw) — free to members.</summary>
        MembersFree,

        /// <summary>Free to visit for everyone (an open ruin or a free-entry site).</summary>
        FreeEntry,

        /// <summary>A managed, pay-on-the-day attraction with no membership route in.</summary>
        Paid,
    }
}