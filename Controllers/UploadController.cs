using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;
using OfficeOpenXml;
using System.Text.Json;

namespace CarbonTrack.Controllers
{
    public class UploadController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<UploadController> _logger;

        // Header name → canonical field, checked as lowercase-normalized substrings
        private static readonly (string[] Patterns, string Field)[] HeaderPatterns =
        {
            (new[]{ "date","when","day","travel date","trip date","departure date","journey date" }, "date"),
            (new[]{ "from","origin","departure","start","source","from city","depart","leaving","leaving from" }, "origin"),
            (new[]{ "to","destination","arrival","end","dest","to city","arriving","arriving at","going to" }, "destination"),
            (new[]{ "mode","transport","vehicle","type","travel type","method","travel mode","trip type","by" }, "mode"),
            (new[]{ "distance","km","kilometres","kilometers","miles","mileage","dist","distance (km)","distance (miles)","journey distance" }, "distance"),
            (new[]{ "pax","passengers","people","travellers","headcount","occupants","person","no. of passengers" }, "passengers"),
            (new[]{ "class","cabin","seat","ticket class","travel class","booking class" }, "class"),
        };

        public UploadController(CarbonTrackContext context, ILogger<UploadController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET /Upload
        public IActionResult Index()
        {
            if (TempData["Success"] is string s) ViewBag.Success = s;
            if (TempData["Error"]   is string e) ViewBag.Error   = e;
            return View();
        }

        // POST /Upload/Parse  — returns JSON preview
        [HttpPost]
        [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB
        public async Task<IActionResult> Parse(IFormFile? file, bool includeWtt = false, bool includeRfi = false)
        {
            if (file == null || file.Length == 0)
                return Json(new { success = false, error = "No file received." });

            string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (ext != ".xlsx" && ext != ".csv")
                return Json(new { success = false, error = "Only .xlsx and .csv files are supported." });

            try
            {
                List<List<string>> rawRows;

                if (ext == ".xlsx")
                    rawRows = await ParseExcel(file);
                else
                    rawRows = await ParseCsv(file);

                if (rawRows.Count < 2)
                    return Json(new { success = false, error = "File contains fewer than 2 rows (header + data)." });

                var headers = rawRows[0];
                var colMap  = DetectColumns(headers);
                var result  = new ParseResult
                {
                    Success         = true,
                    DetectedHeaders = headers,
                    ColumnMap       = colMap,
                };

                for (int i = 1; i < rawRows.Count; i++)
                {
                    var row = rawRows[i];
                    if (row.All(string.IsNullOrWhiteSpace)) continue; // skip blank rows

                    var bulk = ProcessRow(row, colMap, i + 1, includeWtt, includeRfi);
                    result.Rows.Add(bulk);
                }

                _logger.LogInformation("Upload parsed: {Total} rows, {Ready} ready, {Review} review, {Gap} gaps",
                    result.Rows.Count, result.ReadyCount, result.ReviewCount, result.GapCount);

                return Json(new
                {
                    success      = true,
                    headers,
                    columnMap    = colMap,
                    rows         = result.Rows.Select(SerializeRow),
                    readyCount   = result.ReadyCount,
                    reviewCount  = result.ReviewCount,
                    gapCount     = result.GapCount,
                    totalKgCO2e  = result.TotalKgCO2e,
                    totalTCO2e   = Math.Round(result.TotalKgCO2e / 1000, 4),
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Upload parse failed for {FileName}", file.FileName);
                return Json(new { success = false, error = $"Parse failed: {ex.Message}" });
            }
        }

        // POST /Upload/Confirm — bulk import rows to DB
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm([FromBody] ConfirmRequest req)
        {
            if (req?.Rows == null || req.Rows.Count == 0)
            {
                TempData["Error"] = "No rows to import.";
                return Json(new { success = false, error = "No rows to import." });
            }

            try
            {
                var org = await _context.Organisations.FirstOrDefaultAsync(o => o.Id == 1)
                          ?? new Organisation { Id = 1, Name = "Default Organisation" };

                var trips = new List<Trip>();
                foreach (var r in req.Rows)
                {
                    if (!DateTime.TryParse(r.TripDate, out var dt)) continue;
                    if (r.DistanceKm <= 0 || string.IsNullOrWhiteSpace(r.TransportMode)) continue;

                    int year = dt.Year;
                    double ef  = DefraFactorTables.GetFactor(r.TransportMode, year);
                    double wtt = req.IncludeWtt ? DefraFactorTables.GetWttFactor(r.TransportMode, year) : 0;
                    double totalEf = ef + wtt;

                    double kgCO2e = Math.Round(r.DistanceKm * totalEf * r.Passengers, 2);

                    bool isFlight = r.TransportMode.StartsWith("Flight");
                    if (req.IncludeRfi && isFlight)
                        kgCO2e = Math.Round(kgCO2e * DefraFactorTables.FlightRfiMultiplier, 2);

                    string formula = req.IncludeRfi && isFlight
                        ? $"{r.DistanceKm:F2} km × {totalEf:F6} kgCO₂e/km × {r.Passengers} pax × {DefraFactorTables.FlightRfiMultiplier} RFI"
                        : req.IncludeWtt
                            ? $"{r.DistanceKm:F2} km × ({ef:F6} + {wtt:F6} WTT) kgCO₂e/km × {r.Passengers} pax"
                            : $"{r.DistanceKm:F2} km × {ef:F6} kgCO₂e/km × {r.Passengers} pax";

                    string methodology = r.TransportMode.StartsWith("Flight")
                        ? "Haversine Great Circle"
                        : string.IsNullOrWhiteSpace(r.Methodology) ? "Uploaded – source data" : r.Methodology;

                    trips.Add(new Trip
                    {
                        Origin               = r.Origin.Trim(),
                        Destination          = r.Destination.Trim(),
                        TripDate             = dt,
                        TransportMode        = r.TransportMode,
                        TravelClass          = r.TravelClass,
                        Passengers           = Math.Clamp(r.Passengers, 1, 500),
                        DistanceKm           = Math.Round(r.DistanceKm, 2),
                        EmissionFactor       = totalEf,
                        KgCO2e               = kgCO2e,
                        DistanceMethodology  = methodology,
                        DefraFactorYear      = DefraFactorTables.GetDefraLabel(year),
                        Formula              = formula,
                        CreatedAt            = DateTime.UtcNow,
                        OrganisationId       = 1,
                    });
                }

                if (trips.Count == 0)
                {
                    return Json(new { success = false, error = "No valid rows could be imported. Check required fields." });
                }

                await _context.Trips.AddRangeAsync(trips);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Bulk upload: {Count} trips imported", trips.Count);
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

        // ── Parsing helpers ───────────────────────────────────────────────────

        private static async Task<List<List<string>>> ParseExcel(IFormFile file)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            var rows = new List<List<string>>();

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            using var pkg = new ExcelPackage(stream);

            var ws = pkg.Workbook.Worksheets.FirstOrDefault();
            if (ws == null || ws.Dimension == null) return rows;

            int maxRow = ws.Dimension.End.Row;
            int maxCol = ws.Dimension.End.Column;

            for (int r = 1; r <= maxRow; r++)
            {
                var row = new List<string>();
                for (int c = 1; c <= maxCol; c++)
                    row.Add(ws.Cells[r, c].Text ?? "");
                rows.Add(row);
            }
            return rows;
        }

        private static async Task<List<List<string>>> ParseCsv(IFormFile file)
        {
            var rows = new List<List<string>>();
            using var reader = new System.IO.StreamReader(file.OpenReadStream());

            string? line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                rows.Add(SplitCsvLine(line));
            }
            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                    else inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields;
        }

        // ── Column detection ──────────────────────────────────────────────────

        private static ColumnMap DetectColumns(List<string> headers)
        {
            var map = new ColumnMap();
            bool distanceMiles = false;

            for (int i = 0; i < headers.Count; i++)
            {
                string h = headers[i].ToLowerInvariant().Trim();
                foreach (var (patterns, field) in HeaderPatterns)
                {
                    if (patterns.Any(p => h.Contains(p)))
                    {
                        switch (field)
                        {
                            case "date":       map.DateCol        ??= i; break;
                            case "origin":     map.OriginCol      ??= i; break;
                            case "destination":map.DestCol        ??= i; break;
                            case "mode":       map.ModeCol        ??= i; break;
                            case "distance":
                                map.DistanceCol ??= i;
                                if (h.Contains("mile")) distanceMiles = true;
                                break;
                            case "passengers": map.PassengersCol  ??= i; break;
                            case "class":      map.ClassCol       ??= i; break;
                        }
                        break;
                    }
                }
            }
            map.DistanceIsMiles = distanceMiles;
            return map;
        }

        // ── Row processing ────────────────────────────────────────────────────

        private static BulkRow ProcessRow(List<string> cells, ColumnMap col, int rowNum,
                                          bool includeWtt, bool includeRfi)
        {
            string Cell(int? idx) => idx.HasValue && idx.Value < cells.Count ? cells[idx.Value].Trim() : "";

            var bulk = new BulkRow
            {
                SourceRowNumber = rowNum,
                RawDate         = Cell(col.DateCol),
                RawOrigin       = Cell(col.OriginCol),
                RawDestination  = Cell(col.DestCol),
                RawMode         = Cell(col.ModeCol),
                RawDistance     = Cell(col.DistanceCol),
                RawPassengers   = Cell(col.PassengersCol),
                RawClass        = Cell(col.ClassCol),
            };

            // Date
            if (DateTime.TryParse(bulk.RawDate, out var dt))
                bulk.TripDate = dt;
            else
                bulk.Issues.Add("Date missing or unrecognised");

            // Origin / Destination
            bulk.Origin      = bulk.RawOrigin      ?? "";
            bulk.Destination = bulk.RawDestination ?? "";
            if (string.IsNullOrWhiteSpace(bulk.Origin))      bulk.Issues.Add("Origin missing");
            if (string.IsNullOrWhiteSpace(bulk.Destination)) bulk.Issues.Add("Destination missing");

            // Passengers
            if (int.TryParse(bulk.RawPassengers, out int pax) && pax > 0)
                bulk.Passengers = Math.Min(pax, 500);

            // Distance
            if (double.TryParse(bulk.RawDistance, System.Globalization.NumberStyles.Any,
                                 System.Globalization.CultureInfo.InvariantCulture, out double dist) && dist > 0)
            {
                bulk.DistanceKm    = col.DistanceIsMiles ? Math.Round(dist * 1.60934, 2) : Math.Round(dist, 2);
                bulk.DistanceMiles = col.DistanceIsMiles;
            }
            else
                bulk.Issues.Add("Distance missing — will need manual entry or API lookup");

            // Transport mode
            (string modeCode, string confidence) = DefraFactorTables.MapVehicleText(bulk.RawMode ?? "", bulk.DistanceKm);
            bulk.TransportMode  = modeCode;
            bulk.ModeConfidence = confidence;
            bulk.TravelClass    = string.IsNullOrWhiteSpace(bulk.RawClass) ? null : bulk.RawClass;

            if (string.IsNullOrWhiteSpace(modeCode))
                bulk.Issues.Add($"Transport mode not recognised: \"{bulk.RawMode}\"");

            // Calculate if we have enough data
            if (bulk.TripDate.HasValue && !string.IsNullOrWhiteSpace(modeCode) && bulk.DistanceKm > 0)
            {
                int year = bulk.TripDate.Value.Year;
                // For dates earlier than 2024, use 2024 factors with a warning
                if (year < 2024)
                {
                    bulk.Issues.Add($"Date is {year}: no DEFRA factor table available prior to 2024 — using 2024 factors as fallback");
                    year = 2024;
                }

                double ef  = DefraFactorTables.GetFactor(modeCode, year);
                double wtt = includeWtt ? DefraFactorTables.GetWttFactor(modeCode, year) : 0;

                if (ef == 0)
                {
                    bulk.Issues.Add($"No DEFRA factor found for mode \"{modeCode}\"");
                }
                else
                {
                    double totalEf = ef + wtt;
                    bulk.EmissionFactor = ef;
                    bulk.WttFactor      = wtt;
                    bulk.KgCO2e         = Math.Round(bulk.DistanceKm * totalEf * bulk.Passengers, 2);
                    bulk.KgCO2eWtt      = includeWtt ? Math.Round(bulk.DistanceKm * wtt * bulk.Passengers, 4) : 0;
                    bulk.DefraYear      = DefraFactorTables.GetDefraLabel(bulk.TripDate.Value.Year);
                    bulk.Methodology    = DefraCalculator.GetDistanceMethodology(modeCode);

                    bool isFlight = modeCode.StartsWith("Flight");
                    if (includeRfi && isFlight)
                        bulk.KgCO2eRfi = Math.Round(bulk.KgCO2e * DefraFactorTables.FlightRfiMultiplier, 2);

                    bulk.Formula = includeRfi && isFlight
                        ? $"{bulk.DistanceKm:F2} km × {totalEf:F6} kgCO₂e/km × {bulk.Passengers} pax × {DefraFactorTables.FlightRfiMultiplier} RFI"
                        : includeWtt
                            ? $"{bulk.DistanceKm:F2} km × ({ef:F6}+{wtt:F6}WTT) × {bulk.Passengers} pax"
                            : $"{bulk.DistanceKm:F2} km × {ef:F6} kgCO₂e/km × {bulk.Passengers} pax";
                }
            }

            // Determine row status
            if (bulk.Issues.Count == 0)
                bulk.Status = RowStatus.Ready;
            else if (bulk.KgCO2e > 0 && bulk.Issues.All(i => i.Contains("Date is ") || i.Contains("miles") || i.Contains("confidence")))
                bulk.Status = RowStatus.NeedsReview;
            else if (bulk.KgCO2e > 0 && bulk.Issues.Count <= 2 && !bulk.Issues.Any(i => i.Contains("missing")))
                bulk.Status = RowStatus.NeedsReview;
            else if (bulk.KgCO2e == 0)
                bulk.Status = RowStatus.Gap;
            else
                bulk.Status = RowStatus.NeedsReview;

            return bulk;
        }

        private static object SerializeRow(BulkRow r) => new
        {
            sourceRow      = r.SourceRowNumber,
            status         = r.Status.ToString().ToLower(),
            tripDate       = r.TripDate?.ToString("yyyy-MM-dd") ?? "",
            tripDateFmt    = r.TripDate?.ToString("dd MMM yyyy") ?? r.RawDate ?? "",
            origin         = r.Origin,
            destination    = r.Destination,
            transportMode  = r.TransportMode,
            travelClass    = r.TravelClass,
            passengers     = r.Passengers,
            distanceKm     = r.DistanceKm,
            distanceMiles  = r.DistanceMiles,
            emissionFactor = r.EmissionFactor,
            wttFactor      = r.WttFactor,
            kgCO2e         = r.KgCO2e,
            kgCO2eRfi      = r.KgCO2eRfi,
            formula        = r.Formula,
            defraYear      = r.DefraYear,
            methodology    = r.Methodology,
            modeConfidence = r.ModeConfidence,
            issues         = r.Issues,
        };
    }

    // Request body model for confirm endpoint
    public class ConfirmRequest
    {
        public bool IncludeWtt { get; set; }
        public bool IncludeRfi { get; set; }
        public List<ConfirmRow> Rows { get; set; } = new();
    }

    public class ConfirmRow
    {
        public string TripDate      { get; set; } = "";
        public string Origin        { get; set; } = "";
        public string Destination   { get; set; } = "";
        public string TransportMode { get; set; } = "";
        public string? TravelClass  { get; set; }
        public int    Passengers    { get; set; } = 1;
        public double DistanceKm    { get; set; }
        public string? Methodology  { get; set; }
    }
}
