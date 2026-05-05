namespace CarbonTrack.Models
{
    public static class DefraCalculator
    {
        // DEFRA 2025 GHG Conversion Factors — Table 5 Business Travel (kgCO2e per km)
        private static readonly Dictionary<string, double> EmissionFactors = new()
        {
            { "Flight-Domestic",            0.255133 },
            { "Flight-ShortHaul-Economy",   0.153180 },
            { "Flight-ShortHaul-Business",  0.229770 },
            { "Flight-LongHaul-Economy",    0.147613 },
            { "Flight-LongHaul-Business",   0.429234 },
            { "Flight-LongHaul-First",      0.590454 },
            { "Train-National",             0.035390 },
            { "Train-International",        0.004130 },
            { "Car-Petrol",                 0.168330 },
            { "Car-Diesel",                 0.163050 },
            { "Car-Hybrid",                 0.106850 },
            { "Car-Electric",               0.052690 },
            { "Taxi",                       0.149780 },
            { "Ferry-Foot",                 0.018820 },
            { "Ferry-Car",                  0.128860 },
        };

        public static double GetEmissionFactor(string? transportMode, string? travelClass)
        {
            if (string.IsNullOrWhiteSpace(transportMode)) return 0;
            return EmissionFactors.TryGetValue(transportMode, out double factor) ? factor : 0;
        }

        public static double CalculateKgCO2e(double distanceKm, double emissionFactor, int passengers)
        {
            if (distanceKm <= 0 || emissionFactor <= 0 || passengers <= 0) return 0;
            return Math.Round(distanceKm * emissionFactor * passengers, 2);
        }

        public static string GetFormula(double distanceKm, double emissionFactor, int passengers)
        {
            return $"{distanceKm:F2} km × {emissionFactor} kgCO₂e/km × {passengers} pax";
        }

        public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371;
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                     + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                     * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return Math.Round(R * c, 2);
        }

        public static string GetDistanceMethodology(string? transportMode)
        {
            if (string.IsNullOrWhiteSpace(transportMode)) return "Unknown";
            return transportMode.StartsWith("Flight")
                ? "Haversine Great Circle"
                : "Google Maps Distance Matrix";
        }

        public static IReadOnlyDictionary<string, double> AllFactors => EmissionFactors;
    }
}
