using System;

namespace AalgTrips.Models
{
    /// <summary>
    /// Great-circle ("as the crow flies") distance between two latitude/longitude
    /// points. The castle list is ordered by distance from home, and the site takes
    /// no dependency on a routing service, so straight-line distance is used rather
    /// than road distance.
    /// </summary>
    public static class GeoDistance
    {
        // Mean Earth radius in miles, for the haversine formula.
        private const double EarthRadiusMiles = 3958.7613;

        /// <summary>
        /// Computes the great-circle distance between two points, in miles.
        /// </summary>
        /// <param name="lat1">Latitude of the first point, in degrees.</param>
        /// <param name="lon1">Longitude of the first point, in degrees.</param>
        /// <param name="lat2">Latitude of the second point, in degrees.</param>
        /// <param name="lon2">Longitude of the second point, in degrees.</param>
        /// <returns>The distance between the two points in miles.</returns>
        public static double Miles(double lat1, double lon1, double lat2, double lon2)
        {
            double dLat = ToRadians(lat2 - lat1);
            double dLon = ToRadians(lon2 - lon1);

            double a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
                + (Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));

            return EarthRadiusMiles * 2 * Math.Asin(Math.Min(1.0, Math.Sqrt(a)));
        }

        private static double ToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}