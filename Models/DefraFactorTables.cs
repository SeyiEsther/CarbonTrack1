namespace CarbonTrack.Models
{
    /// <summary>
    /// Multi-year DEFRA/BEIS GHG Conversion Factor tables for Business Travel (Table 5).
    /// Factors in kgCO2e per passenger-kilometre, AR5 GWP100 basis.
    /// Sources:
    ///   2024: DEFRA/BEIS "Greenhouse Gas Reporting: Conversion Factors 2024" v1.0
    ///   2025: DEFRA/BEIS "Greenhouse Gas Reporting: Conversion Factors 2025/26" v1.0
    /// </summary>
    public static class DefraFactorTables
    {
        // ── Combustion (direct) emission factors ─────────────────────────────

        public static readonly IReadOnlyDictionary<string, double> Factors2024 = new Dictionary<string, double>
        {
            { "Flight-Domestic",            0.24503 },
            { "Flight-ShortHaul-Economy",   0.15302 },
            { "Flight-ShortHaul-Business",  0.22953 },
            { "Flight-LongHaul-Economy",    0.14778 },
            { "Flight-LongHaul-Business",   0.42965 },
            { "Flight-LongHaul-First",      0.59147 },
            { "Train-National",             0.03549 },
            { "Train-International",        0.00601 },
            { "Car-Petrol",                 0.17008 },
            { "Car-Diesel",                 0.16387 },
            { "Car-Hybrid",                 0.11672 },
            { "Car-Electric",               0.05349 },
            { "Taxi",                       0.14914 },
            { "Ferry-Foot",                 0.01868 },
            { "Ferry-Car",                  0.12964 },
        };

        public static readonly IReadOnlyDictionary<string, double> Factors2025 = new Dictionary<string, double>
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

        // ── Well-to-Tank (WTT / upstream) factors — kgCO2e per passenger-km ─
        // Source: DEFRA Scope 3 WTT Conversion Factors, same publication year

        public static readonly IReadOnlyDictionary<string, double> Wtt2024 = new Dictionary<string, double>
        {
            { "Flight-Domestic",            0.02793 },
            { "Flight-ShortHaul-Economy",   0.01744 },
            { "Flight-ShortHaul-Business",  0.02616 },
            { "Flight-LongHaul-Economy",    0.01684 },
            { "Flight-LongHaul-Business",   0.04895 },
            { "Flight-LongHaul-First",      0.06740 },
            { "Train-National",             0.00447 },
            { "Train-International",        0.00068 },
            { "Car-Petrol",                 0.02734 },
            { "Car-Diesel",                 0.00631 },
            { "Car-Hybrid",                 0.01733 },
            { "Car-Electric",               0.01349 },
            { "Taxi",                       0.02429 },
            { "Ferry-Foot",                 0.00302 },
            { "Ferry-Car",                  0.02074 },
        };

        public static readonly IReadOnlyDictionary<string, double> Wtt2025 = new Dictionary<string, double>
        {
            { "Flight-Domestic",            0.02907 },
            { "Flight-ShortHaul-Economy",   0.01746 },
            { "Flight-ShortHaul-Business",  0.02619 },
            { "Flight-LongHaul-Economy",    0.01683 },
            { "Flight-LongHaul-Business",   0.04892 },
            { "Flight-LongHaul-First",      0.06731 },
            { "Train-National",             0.00423 },
            { "Train-International",        0.00047 },
            { "Car-Petrol",                 0.02587 },
            { "Car-Diesel",                 0.00597 },
            { "Car-Hybrid",                 0.01597 },
            { "Car-Electric",               0.01290 },
            { "Taxi",                       0.02301 },
            { "Ferry-Foot",                 0.00285 },
            { "Ferry-Car",                  0.01960 },
        };

        // ── Radiative Forcing Index (RFI) for flights ─────────────────────────
        // DEFRA guidance: 1.891x multiplier applied to flight combustion factors only.
        // Not mandated by DEFRA 2025 standard; provided for organisations that
        // choose to apply it per GHG Protocol or client CRP requirements.
        public const double FlightRfiMultiplier = 1.891;

        // ── Helpers ────────────────────────────────────────────────────────────

        public static IReadOnlyDictionary<string, double> GetFactors(int year) =>
            year <= 2024 ? Factors2024 : Factors2025;

        public static IReadOnlyDictionary<string, double> GetWttFactors(int year) =>
            year <= 2024 ? Wtt2024 : Wtt2025;

        public static string GetDefraLabel(int year) =>
            year <= 2024 ? "DEFRA 2024" : "DEFRA 2025";

        public static double GetFactor(string mode, int year)
        {
            var table = GetFactors(year);
            return table.TryGetValue(mode, out double f) ? f : 0;
        }

        public static double GetWttFactor(string mode, int year)
        {
            var table = GetWttFactors(year);
            return table.TryGetValue(mode, out double f) ? f : 0;
        }

        // Infer the haul type for flights based on distance and class hint
        public static string InferFlightMode(double distanceKm, string? classHint)
        {
            string cls = (classHint ?? "economy").ToLowerInvariant();
            bool isBusiness = cls.Contains("business") || cls.Contains("club");
            bool isFirst    = cls.Contains("first");

            if (distanceKm < 800)
                return "Flight-Domestic";
            if (distanceKm <= 4000)
                return isBusiness ? "Flight-ShortHaul-Business" : "Flight-ShortHaul-Economy";
            return isFirst ? "Flight-LongHaul-First"
                 : isBusiness ? "Flight-LongHaul-Business"
                 : "Flight-LongHaul-Economy";
        }

        // Map a free-text vehicle/mode description to a CT mode code
        public static (string mode, string confidence) MapVehicleText(string raw, double distanceKm = 0)
        {
            string s = raw.ToLowerInvariant().Trim();

            if (s.Contains("flight") || s.Contains("plane") || s.Contains("air") || s.Contains("flew"))
            {
                if (s.Contains("domestic"))                      return ("Flight-Domestic", "high");
                if (s.Contains("short") || s.Contains("eu"))    return ("Flight-ShortHaul-Economy", "high");
                if (s.Contains("long") || s.Contains("intercont")) return ("Flight-LongHaul-Economy", "high");
                if (s.Contains("business"))                      return ("Flight-ShortHaul-Business", "medium");
                if (s.Contains("first"))                         return ("Flight-LongHaul-First", "medium");
                string inferred = distanceKm > 0 ? InferFlightMode(distanceKm, s) : "Flight-LongHaul-Economy";
                return (inferred, distanceKm > 0 ? "medium" : "low");
            }

            if (s.Contains("eurostar") || s.Contains("international") && (s.Contains("train") || s.Contains("rail")))
                return ("Train-International", "high");
            if (s.Contains("train") || s.Contains("rail") || s.Contains("national rail") || s.Contains("overground") || s.Contains("tram"))
                return ("Train-National", "high");

            if (s.Contains("electric") || s.Contains("bev") || s.Contains(" ev ") || s.Contains("ev,") || s.StartsWith("ev"))
                return ("Car-Electric", "high");
            if (s.Contains("hybrid"))
                return ("Car-Hybrid", "high");
            if (s.Contains("diesel"))
                return ("Car-Diesel", "high");
            if (s.Contains("taxi") || s.Contains("cab") || s.Contains("uber") || s.Contains("bolt") || s.Contains("black car"))
                return ("Taxi", "high");
            if (s.Contains("car") || s.Contains("drive") || s.Contains("drove") || s.Contains("vehicle") || s.Contains("petrol"))
                return ("Car-Petrol", s.Contains("petrol") ? "high" : "medium");

            if (s.Contains("ferry") || s.Contains("boat") || s.Contains("ship") || s.Contains("sea"))
            {
                string mode = (s.Contains("foot") || s.Contains("walk") || s.Contains("passenger")) ? "Ferry-Foot" : "Ferry-Car";
                return (mode, "medium");
            }

            return ("", "none");
        }

        // All known mode codes
        public static readonly IReadOnlyList<string> AllModes = new[]
        {
            "Flight-Domestic", "Flight-ShortHaul-Economy", "Flight-ShortHaul-Business",
            "Flight-LongHaul-Economy", "Flight-LongHaul-Business", "Flight-LongHaul-First",
            "Train-National", "Train-International",
            "Car-Petrol", "Car-Diesel", "Car-Hybrid", "Car-Electric", "Taxi",
            "Ferry-Foot", "Ferry-Car",
        };
    }
}
