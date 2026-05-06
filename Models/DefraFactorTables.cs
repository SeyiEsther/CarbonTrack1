namespace CarbonTrack.Models
{
    /// <summary>
    /// Holds DESNZ/DEFRA WTW (Well-to-Wheel = TTW + WTT) emission factor tables.
    ///
    /// DESNZ 2024 WTW factors (used for uploads):
    ///   Source: DESNZ "Greenhouse Gas Conversion Factors for Company Reporting 2024"
    ///   Table: Business Travel — TTW + WTT combined, AR5 GWP100 basis.
    ///
    /// DEFRA 2025 factors (used for manually-logged trips):
    ///   Source: DEFRA/BEIS "GHG Conversion Factors 2025/26" v1.0
    ///
    /// Key distinction:
    ///   • Cars (Average Car): unit is vehicle-km  → kgCO2e = distance × EF         (no passenger multiplier)
    ///   • All other modes:    unit is passenger-km → kgCO2e = distance × EF × pax   (passenger multiplier applied)
    /// </summary>
    public static class DefraFactorTables
    {
        // ── DESNZ 2024 WTW factor record ──────────────────────────────────────
        // Total = WTW total kgCO2e/unit
        // CO2 / CH4 / N2O = TTW component breakdown (combustion only)
        // IsVehicleKm = true → no passenger multiplier (car); false → multiply by passengers
        public record WtwFactor(double Total, double CO2, double CH4, double N2O, bool IsVehicleKm);

        /// <summary>
        /// DESNZ 2024 WTW factors — use these for bulk uploads.
        /// Keys match CT TransportMode codes.
        /// </summary>
        public static readonly IReadOnlyDictionary<string, WtwFactor> Desnz2024 =
            new Dictionary<string, WtwFactor>
        {
            // Flights — passenger.km  (multiply by passengers)
            { "Flight-Domestic",          new(0.30607, 0.27101, 0.00022, 0.00134, false) },
            { "Flight-ShortHaul-Economy", new(0.20878, 0.18499, 0.00001, 0.00092, false) },
            { "Flight-LongHaul-Economy",  new(0.29341, 0.25998, 0.00001, 0.00129, false) },
            // Car — vehicle km  (do NOT multiply by passengers; car burns same fuel regardless of occupancy)
            { "Car-Average",              new(0.21090, 0.16574, 0.00019, 0.00098, true)  },
            // Rail — passenger.km
            { "Train-National",           new(0.04443, 0.03510, 0.00008, 0.00028, false) },
            { "Train-International",      new(0.00563, 0.00441, 0.00002, 0.00003, false) },
        };

        // ── DEFRA 2025 combustion-only factors (for manually-logged trips) ────
        public static readonly IReadOnlyDictionary<string, double> Factors2025 =
            new Dictionary<string, double>
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

        // ── DEFRA 2024 combustion-only factors (legacy / fallback) ────────────
        public static readonly IReadOnlyDictionary<string, double> Factors2024 =
            new Dictionary<string, double>
        {
            { "Flight-Domestic",            0.24503  },
            { "Flight-ShortHaul-Economy",   0.15302  },
            { "Flight-ShortHaul-Business",  0.22953  },
            { "Flight-LongHaul-Economy",    0.14778  },
            { "Flight-LongHaul-Business",   0.42965  },
            { "Flight-LongHaul-First",      0.59147  },
            { "Train-National",             0.03549  },
            { "Train-International",        0.00601  },
            { "Car-Petrol",                 0.17008  },
            { "Car-Diesel",                 0.16387  },
            { "Car-Hybrid",                 0.11672  },
            { "Car-Electric",               0.05349  },
            { "Taxi",                       0.14914  },
            { "Ferry-Foot",                 0.01868  },
            { "Ferry-Car",                  0.12964  },
        };

        // ── Helpers ────────────────────────────────────────────────────────────

        public static IReadOnlyDictionary<string, double> GetFactors(int year) =>
            year <= 2024 ? Factors2024 : Factors2025;

        public static string GetDefraLabel(int year) =>
            year <= 2024 ? "DESNZ 2024 WTW" : "DEFRA 2025";

        public static double GetFactor(string mode, int year)
        {
            var table = GetFactors(year);
            return table.TryGetValue(mode, out double f) ? f : 0;
        }

        // ── UK cities for domestic flight detection ────────────────────────────
        public static readonly HashSet<string> UkCities = new(StringComparer.OrdinalIgnoreCase)
        {
            "london","manchester","birmingham","glasgow","edinburgh","bristol","leeds",
            "liverpool","sheffield","cardiff","newcastle","nottingham","southampton",
            "brighton","coventry","hull","plymouth","exeter","york","oxford","cambridge",
            "reading","luton","newquay","aberdeen","inverness","dundee","belfast",
            "portsmouth","leicester","stoke","wolverhampton","sunderland","norwich",
            "milton keynes","derby","fareham","telford","smethwick","hayes","llanelli",
            "heathrow","gatwick","stansted","bristol airport",
        };

        // ── Journey string parsing ─────────────────────────────────────────────
        // Input examples: "Plymouth - London", "London - Dallas - London", "London - Dallas - London "
        public static (string origin, string destination, string via, bool isReturn)
            ParseJourney(string raw)
        {
            var parts = raw.Split('-')
                           .Select(p => p.Trim())
                           .Where(p => !string.IsNullOrWhiteSpace(p))
                           .ToList();

            if (parts.Count == 0) return ("", "", "", false);
            if (parts.Count == 1) return (parts[0], parts[0], "", false);

            string origin = parts[0];
            string dest   = parts[^1];
            string via    = parts.Count >= 3 ? parts[1] : "";

            // Return trip: first and last city are same (normalise for typos)
            bool isReturn = NormaliseCity(origin) == NormaliseCity(dest);
            // If return trip, the "destination" for reporting purposes is the via city
            string reportDest = isReturn && !string.IsNullOrWhiteSpace(via) ? via : dest;

            return (origin, reportDest, via, isReturn);
        }

        // ── Flight haul classification ─────────────────────────────────────────
        // Input: intermediate/destination city, one-way distance in km
        // Returns a CT mode code for the flight
        public static string InferFlightMode(string viaOrDest, double onewayKm)
        {
            string city = NormaliseCity(viaOrDest);

            // Known UK destinations → domestic
            if (UkCities.Contains(city) && onewayKm < 1500)
                return "Flight-Domestic";

            // Distance-based: 3700 km one-way ≈ 2300 miles = start of long-haul
            if (onewayKm <= 3700) return "Flight-ShortHaul-Economy";
            return "Flight-LongHaul-Economy";
        }

        // ── Vehicle text → DESNZ mode code + WTW factor ───────────────────────
        // Returns CT mode code and confidence level
        public static (string mode, string confidence) MapRawMode(
            string rawMode, string viaOrDest, double totalMiles)
        {
            string s = rawMode.ToLowerInvariant().Trim();

            if (s.Contains("car") || s.Contains("drive") || s.Contains("drove"))
                return ("Car-Average", "high");

            if (s.Contains("train") || s.Contains("rail"))
            {
                // Simple heuristic: if via city is outside UK → international rail
                bool intl = !string.IsNullOrWhiteSpace(viaOrDest) &&
                            !UkCities.Contains(NormaliseCity(viaOrDest));
                return (intl ? "Train-International" : "Train-National", "high");
            }

            if (s.Contains("plane") || s.Contains("flight") || s.Contains("air") || s.Contains("flew"))
            {
                // one-way km: if total miles represents a return trip, divide by 2
                // We don't know here whether it's return — caller resolves
                double onewayKm = (totalMiles / 0.621);
                string flightMode = InferFlightMode(viaOrDest, onewayKm / 2); // assume return = /2
                return (flightMode, "medium");
            }

            return ("", "none");
        }

        // ── Date parsing ───────────────────────────────────────────────────────
        // Handles: "YY MM DD" (e.g., "24 01 22"), plus standard formats
        public static DateTime? ParseFlexDate(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();

            // YY MM DD with spaces
            var spaceParts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (spaceParts.Length == 3 &&
                int.TryParse(spaceParts[0], out int yy) && yy >= 0 && yy <= 99 &&
                int.TryParse(spaceParts[1], out int mm) && mm >= 1 && mm <= 12 &&
                int.TryParse(spaceParts[2], out int dd) && dd >= 1 && dd <= 31)
            {
                try { return new DateTime(2000 + yy, mm, dd); }
                catch { /* invalid date */ }
            }

            // Standard formats
            string[] fmts = { "dd/MM/yyyy","d/M/yyyy","yyyy-MM-dd","dd-MM-yyyy",
                               "MM/dd/yyyy","d/M/yy","dd/MM/yy" };
            if (DateTime.TryParseExact(raw, fmts,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt1)) return dt1;

            if (DateTime.TryParse(raw, out var dt2)) return dt2;
            return null;
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static string NormaliseCity(string s) =>
            s.ToLowerInvariant().Trim()
             .Replace("lodon", "london")   // common typo in real data
             .Replace("mancester", "manchester")
             .Replace("switerland", "switzerland");

        public static readonly IReadOnlyList<string> AllModes = new[]
        {
            "Flight-Domestic","Flight-ShortHaul-Economy","Flight-ShortHaul-Business",
            "Flight-LongHaul-Economy","Flight-LongHaul-Business","Flight-LongHaul-First",
            "Train-National","Train-International","Car-Average",
            "Car-Petrol","Car-Diesel","Car-Hybrid","Car-Electric","Taxi",
            "Ferry-Foot","Ferry-Car",
        };
    }
}
