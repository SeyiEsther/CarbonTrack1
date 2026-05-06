using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;
using OfficeOpenXml;
using System.Globalization;

namespace CarbonTrack.Controllers
{
    [Authorize(Roles = "Admin,Consultant")]
    public class UploadController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<UploadController> _logger;

        // ── Column header → canonical field ───────────────────────────────────
        // Each entry: pattern substrings to match (lowercase), field name
        private static readonly (string[] Patterns, string Field)[] HeaderPatterns =
        {
            // dateback must be checked before dateout: "date back" contains the substring "date"
            // which would otherwise match the dateout entry's generic "date" pattern first.
            (new[]{ "date back","date_back","return date","back" },                            "dateback"),
            (new[]{ "date out","date_out","departure date","travel date","trip date","date" }, "dateout"),
            (new[]{ "journey","route","itinerary","trip route","from/to" },                   "journey"),
            (new[]{ "from","origin","departure","depart","start city","leaving from" },       "origin"),
            (new[]{ "to","destination","dest","arrival","end city","arriving" },              "dest"),
            (new[]{ "mode","transport","vehicle","travel type","method","by" },               "mode"),
            (new[]{ "miles","mileage","distance (miles)" },                                   "miles"),
            (new[]{ "km","kilometres","kilometers","distance (km)","distance" },              "km"),
            (new[]{ "people","pax","passengers","persons","travellers","headcount" },         "passengers"),
            (new[]{ "class","cabin","seat" },                                                 "class"),
        };

        public UploadController(CarbonTrackContext context, UserManager<ApplicationUser> userManager, ILogger<UploadController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger  = logger;
        }

        // GET /Upload
        public IActionResult Index()
        {
            if (TempData["Success"] is string s) ViewBag.Success = s;
            if (TempData["Error"]   is string e) ViewBag.Error   = e;
            return View();
        }

        // POST /Upload/Parse — parse file, return JSON preview
        [HttpPost]
        [RequestSizeLimit(10 * 1024 * 1024)]
        public async Task<IActionResult> Parse(IFormFile? file)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, error = "No file received." });

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".csv")
                return Json(new { success = false, error = "Only .xlsx and .csv files are supported." });

            try
            {
                List<List<string>> rawRows = ext == ".xlsx"
                    ? await ParseExcel(file)
                    : await ParseCsv(file);

                if (rawRows.Count < 2)
                    return Json(new { success = false, error = "File must have a header row plus at least one data row." });

                var headers = rawRows[0];
                var colMap  = DetectColumns(headers);
                var rows    = new List<BulkRow>();

                for (int i = 1; i < rawRows.Count; i++)
                {
                    var raw = rawRows[i];
                    if (raw.All(string.IsNullOrWhiteSpace)) continue;
                    rows.Add(ProcessRow(raw, colMap, i + 1));
                }

                _logger.LogInformation("Upload parsed: {T} rows — {R} ready, {V} review, {G} gaps",
                    rows.Count,
                    rows.Count(r => r.Status == RowStatus.Ready),
                    rows.Count(r => r.Status == RowStatus.NeedsReview),
                    rows.Count(r => r.Status == RowStatus.Gap));

                double totalKg = rows.Where(r => r.Status != RowStatus.Gap).Sum(r => r.KgCO2e);

                return Json(new
                {
                    success     = true,
                    headers,
                    columnMap   = colMap,
                    rows        = rows.Select(SerialiseRow).ToList(),
                    readyCount  = rows.Count(r => r.Status == RowStatus.Ready),
                    reviewCount = rows.Count(r => r.Status == RowStatus.NeedsReview),
                    gapCount    = rows.Count(r => r.Status == RowStatus.Gap),
                    totalKgCO2e = Math.Round(totalKg, 2),
                    totalTCO2e  = Math.Round(totalKg / 1000, 4),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload parse failed: {File}", file.FileName);
                return Json(new { success = false, error = $"Parse error: {ex.Message}" });
            }
        }

        // POST /Upload/Confirm — bulk insert importable rows
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm([FromBody] ConfirmRequest req)
        {
            if (req?.Rows == null || req.Rows.Count == 0)
                return Json(new { success = false, error = "No rows to import." });

            var currentUser = await _userManager.GetUserAsync(User);
            var orgId = currentUser?.OrganisationId ?? 0;

            try
            {
                var trips = new List<Trip>();

                foreach (var r in req.Rows)
                {
                    if (!DateTime.TryParse(r.TripDate, out var dt)) continue;
                    if (r.DistanceKm <= 0 || string.IsNullOrWhiteSpace(r.TransportMode)) continue;

                    // Re-derive the factor from the DESNZ 2024 table (source of truth)
                    double ef = 0, co2 = 0, ch4 = 0, n2o = 0;
                    bool   isVehicleKm = false;

                    if (DefraFactorTables.Desnz2024.TryGetValue(r.TransportMode, out var f))
                    {
                        ef = f.Total; co2 = f.CO2; ch4 = f.CH4; n2o = f.N2O;
                        isVehicleKm = f.IsVehicleKm;
                    }

                    double kgCO2e = isVehicleKm
                        ? Math.Round(r.DistanceKm * ef, 2)
                        : Math.Round(r.DistanceKm * ef * r.Passengers, 2);

                    double kgCO2 = isVehicleKm
                        ? Math.Round(r.DistanceKm * co2, 4)
                        : Math.Round(r.DistanceKm * co2 * r.Passengers, 4);

                    double kgCH4 = isVehicleKm
                        ? Math.Round(r.DistanceKm * ch4, 6)
                        : Math.Round(r.DistanceKm * ch4 * r.Passengers, 6);

                    double kgN2O = isVehicleKm
                        ? Math.Round(r.DistanceKm * n2o, 6)
                        : Math.Round(r.DistanceKm * n2o * r.Passengers, 6);

                    string formula = isVehicleKm
                        ? $"{r.DistanceKm:F2} km × {ef:F5} kgCO₂e/km (vehicle-km, no pax factor)"
                        : $"{r.DistanceKm:F2} km × {ef:F5} kgCO₂e/km × {r.Passengers} pax";

                    trips.Add(new Trip
                    {
                        Origin              = r.Origin.Trim(),
                        Destination         = r.Destination.Trim(),
                        TripDate            = dt,
                        TransportMode       = r.TransportMode,
                        TravelClass         = r.TravelClass,
                        Passengers          = Math.Clamp(r.Passengers, 1, 500),
                        DistanceKm          = Math.Round(r.DistanceKm, 2),
                        EmissionFactor      = ef,
                        KgCO2e              = kgCO2e,
                        KgCO2               = kgCO2,
                        KgCH4               = kgCH4,
                        KgN2O               = kgN2O,
                        DistanceMethodology = r.Methodology ?? "Uploaded – source data",
                        DefraFactorYear     = "DESNZ 2024 WTW",
                        Formula             = formula,
                        CreatedAt           = DateTime.UtcNow,
                        OrganisationId      = orgId,
                        UserId              = currentUser?.Id,
                    });
                }

                if (trips.Count == 0)
                    return Json(new { success = false, error = "No valid rows could be imported." });

                await _context.Trips.AddRangeAsync(trips);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Bulk import: {Count} trips saved", trips.Count);

                return Json(new
                {
                    success  = true,
                    imported = trips.Count,
                    kgCO2e   = Math.Round(trips.Sum(t => t.KgCO2e), 2),
                    tCO2e    = Math.Round(trips.Sum(t => t.KgCO2e) / 1000, 4),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bulk import failed");
                return Json(new { success = false, error = "Import failed. Please try again." });
            }
        }

        // ── File parsers ───────────────────────────────────────────────────────

        private static async Task<List<List<string>>> ParseExcel(IFormFile file)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var rows = new List<List<string>>();
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            using var pkg = new ExcelPackage(ms);
            var ws = pkg.Workbook.Worksheets.FirstOrDefault();
            if (ws?.Dimension == null) return rows;

            for (int r = 1; r <= ws.Dimension.End.Row; r++)
            {
                var row = new List<string>();
                for (int c = 1; c <= ws.Dimension.End.Column; c++)
                    row.Add(ws.Cells[r, c].Text ?? "");
                rows.Add(row);
            }
            return rows;
        }

        private static async Task<List<List<string>>> ParseCsv(IFormFile file)
        {
            var rows = new List<List<string>>();
            using var reader = new StreamReader(file.OpenReadStream());
            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    rows.Add(SplitCsvLine(line));
            }
            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQ = false;
            var cur = new System.Text.StringBuilder();
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQ && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                    else inQ = !inQ;
                }
                else if (c == ',' && !inQ) { fields.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(c);
            }
            fields.Add(cur.ToString());
            return fields;
        }

        // ── Column detection ────────────────────────────────────────────────────

        private static ColumnMap DetectColumns(List<string> headers)
        {
            var map = new ColumnMap();

            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].ToLowerInvariant().Trim();
                foreach (var (patterns, field) in HeaderPatterns)
                {
                    if (!patterns.Any(p => h == p || h.Contains(p))) continue;
                    switch (field)
                    {
                        case "dateout":    map.DateOutCol    ??= i; break;
                        case "dateback":   map.DateBackCol   ??= i; break;
                        case "journey":    map.JourneyCol    ??= i; break;
                        case "origin":     map.OriginCol     ??= i; break;
                        case "dest":       map.DestCol       ??= i; break;
                        case "mode":       map.ModeCol       ??= i; break;
                        case "miles":      map.DistanceCol   ??= i; map.InputIsMiles = true;  break;
                        case "km":         map.DistanceCol   ??= i; map.InputIsMiles = false; break;
                        case "passengers": map.PassengersCol ??= i; break;
                        case "class":      map.ClassCol      ??= i; break;
                    }
                    break;
                }
            }
            return map;
        }

        // ── Row processing ─────────────────────────────────────────────────────

        private static BulkRow ProcessRow(List<string> cells, ColumnMap col, int rowNum)
        {
            string Cell(int? idx) => idx.HasValue && idx.Value < cells.Count
                ? cells[idx.Value].Trim() : "";

            var bulk = new BulkRow
            {
                SourceRowNumber = rowNum,
                RawDateOut      = Cell(col.DateOutCol),
                RawDateBack     = Cell(col.DateBackCol),
                RawJourney      = Cell(col.JourneyCol),
                RawOrigin       = Cell(col.OriginCol),
                RawDestination  = Cell(col.DestCol),
                RawMode         = Cell(col.ModeCol),
                RawDistance     = Cell(col.DistanceCol),
                RawPassengers   = Cell(col.PassengersCol),
                RawClass        = Cell(col.ClassCol),
                InputWasMiles   = col.InputIsMiles,
            };

            // ── 1. Date ───────────────────────────────────────────────────────
            bulk.TripDate = DefraFactorTables.ParseFlexDate(bulk.RawDateOut ?? "");
            if (bulk.TripDate == null)
                bulk.Issues.Add("Date missing or unrecognised");

            // ── 2. Journey / origin / destination ─────────────────────────────
            if (!string.IsNullOrWhiteSpace(bulk.RawJourney))
            {
                var (origin, dest, via, isReturn) =
                    DefraFactorTables.ParseJourney(bulk.RawJourney);
                bulk.Origin      = origin;
                bulk.Destination = dest;
                bulk.Via         = via;
                bulk.IsReturnTrip = isReturn;
            }
            else
            {
                bulk.Origin      = bulk.RawOrigin      ?? "";
                bulk.Destination = bulk.RawDestination ?? "";
            }

            if (string.IsNullOrWhiteSpace(bulk.Origin))      bulk.Issues.Add("Origin missing");
            if (string.IsNullOrWhiteSpace(bulk.Destination)) bulk.Issues.Add("Destination missing");

            // ── 3. Passengers ──────────────────────────────────────────────────
            if (int.TryParse(bulk.RawPassengers, out int pax) && pax >= 1)
                bulk.Passengers = Math.Min(pax, 500);

            // ── 4. Distance — miles ÷ 0.621 ────────────────────────────────────
            if (double.TryParse(bulk.RawDistance,
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double dist) && dist > 0)
            {
                bulk.DistanceMiles = col.InputIsMiles ? dist : dist * 0.621;
                bulk.DistanceKm    = col.InputIsMiles
                    ? Math.Round(dist / 0.621, 2)   // exact client rule: miles ÷ 0.621
                    : Math.Round(dist, 2);
            }
            else
                bulk.Issues.Add("Distance missing or zero");

            // ── 5. Transport mode → DESNZ mode code ────────────────────────────
            string rawMode = bulk.RawMode ?? "";
            string viaCity = !string.IsNullOrWhiteSpace(bulk.Via)
                ? bulk.Via : bulk.Destination;

            (string modeCode, string confidence) = MapMode(rawMode, viaCity, bulk.DistanceMiles, bulk.IsReturnTrip);
            bulk.TransportMode  = modeCode;
            bulk.ModeConfidence = confidence;
            bulk.TravelClass    = string.IsNullOrWhiteSpace(bulk.RawClass) ? null : bulk.RawClass;

            if (string.IsNullOrWhiteSpace(modeCode))
                bulk.Issues.Add($"Transport mode not recognised: \"{rawMode}\"");

            // ── 6. Calculate emissions ─────────────────────────────────────────
            if (bulk.TripDate != null && !string.IsNullOrWhiteSpace(modeCode) && bulk.DistanceKm > 0)
            {
                if (DefraFactorTables.Desnz2024.TryGetValue(modeCode, out var f))
                {
                    bulk.EmissionFactor = f.Total;
                    bulk.IsVehicleKm    = f.IsVehicleKm;

                    // Car: vehicle-km → no passenger multiplier
                    // All others: passenger-km → multiply by passengers
                    if (f.IsVehicleKm)
                    {
                        bulk.KgCO2e = Math.Round(bulk.DistanceKm * f.Total, 2);
                        bulk.KgCO2  = Math.Round(bulk.DistanceKm * f.CO2,   4);
                        bulk.KgCH4  = Math.Round(bulk.DistanceKm * f.CH4,   6);
                        bulk.KgN2O  = Math.Round(bulk.DistanceKm * f.N2O,   6);
                        bulk.Formula = $"{bulk.DistanceMiles:F1} mi → {bulk.DistanceKm:F2} km × {f.Total:F5} kgCO₂e/km [vehicle-km]";
                    }
                    else
                    {
                        bulk.KgCO2e = Math.Round(bulk.DistanceKm * f.Total * bulk.Passengers, 2);
                        bulk.KgCO2  = Math.Round(bulk.DistanceKm * f.CO2   * bulk.Passengers, 4);
                        bulk.KgCH4  = Math.Round(bulk.DistanceKm * f.CH4   * bulk.Passengers, 6);
                        bulk.KgN2O  = Math.Round(bulk.DistanceKm * f.N2O   * bulk.Passengers, 6);
                        bulk.Formula = $"{bulk.DistanceMiles:F1} mi → {bulk.DistanceKm:F2} km × {f.Total:F5} kgCO₂e/km × {bulk.Passengers} pax";
                    }

                    bulk.DefraYear   = "DESNZ 2024 WTW";
                    bulk.Methodology = modeCode.StartsWith("Flight")
                        ? "Source miles – great circle (÷0.621)"
                        : modeCode.StartsWith("Train")
                            ? "Source miles – rail distance (÷0.621)"
                            : "Source miles – road distance (÷0.621)";
                }
                else
                {
                    bulk.Issues.Add($"No DESNZ 2024 WTW factor for \"{modeCode}\"");
                }
            }

            // ── 7. Row status ──────────────────────────────────────────────────
            bool hasCritical = bulk.Issues.Any(i =>
                i.Contains("missing") || i.Contains("not recog") || i.Contains("No DESNZ"));

            bulk.Status = bulk.KgCO2e > 0 && !hasCritical
                ? (bulk.Issues.Count == 0 ? RowStatus.Ready : RowStatus.NeedsReview)
                : RowStatus.Gap;

            return bulk;
        }

        // ── Mode mapping ───────────────────────────────────────────────────────

        private static (string mode, string confidence) MapMode(
            string rawMode, string viaOrDest, double totalMiles, bool isReturn)
        {
            string s = rawMode.ToLowerInvariant().Trim();

            if (s == "car" || s.Contains("car") || s.Contains("drive") || s.Contains("van"))
                return ("Car-Average", "high");

            if (s == "train" || s.Contains("train") || s.Contains("rail"))
            {
                bool intl = !string.IsNullOrWhiteSpace(viaOrDest) &&
                            !DefraFactorTables.UkCities.Contains(viaOrDest.ToLowerInvariant().Trim());
                return (intl ? "Train-International" : "Train-National", "high");
            }

            if (s == "plane" || s.Contains("plane") || s.Contains("flight") ||
                s.Contains("air")  || s.Contains("flew"))
            {
                // One-way km: if it's a return trip the source miles is total round-trip
                double onewayMiles = isReturn ? totalMiles / 2.0 : totalMiles;
                double onewayKm    = onewayMiles / 0.621;
                string flightMode  = DefraFactorTables.InferFlightMode(viaOrDest, onewayKm);
                return (flightMode, "medium");
            }

            return ("", "none");
        }

        // ── Serialise for JSON response ────────────────────────────────────────

        private static object SerialiseRow(BulkRow r) => new
        {
            sourceRow      = r.SourceRowNumber,
            status         = r.Status.ToString().ToLower(),
            tripDate       = r.TripDate?.ToString("yyyy-MM-dd") ?? "",
            tripDateFmt    = r.TripDate?.ToString("dd MMM yyyy") ?? r.RawDateOut ?? "",
            origin         = r.Origin,
            destination    = r.Destination,
            via            = r.Via,
            isReturnTrip   = r.IsReturnTrip,
            rawJourney     = r.RawJourney ?? $"{r.Origin} → {r.Destination}",
            transportMode  = r.TransportMode,
            passengers     = r.Passengers,
            distanceMiles  = r.DistanceMiles,
            distanceKm     = r.DistanceKm,
            inputWasMiles  = r.InputWasMiles,
            emissionFactor = r.EmissionFactor,
            isVehicleKm    = r.IsVehicleKm,
            kgCO2e         = r.KgCO2e,
            kgCO2          = r.KgCO2,
            kgCH4          = r.KgCH4,
            kgN2O          = r.KgN2O,
            formula        = r.Formula,
            defraYear      = r.DefraYear,
            modeConfidence = r.ModeConfidence,
            issues         = r.Issues,
        };
    }

    // ── Confirm request models ─────────────────────────────────────────────────

    public class ConfirmRequest
    {
        public List<ConfirmRow> Rows { get; set; } = new();
    }

    public class ConfirmRow
    {
        public string  TripDate      { get; set; } = "";
        public string  Origin        { get; set; } = "";
        public string  Destination   { get; set; } = "";
        public string  TransportMode { get; set; } = "";
        public string? TravelClass   { get; set; }
        public int     Passengers    { get; set; } = 1;
        public double  DistanceKm    { get; set; }
        public string? Methodology   { get; set; }
    }
}
