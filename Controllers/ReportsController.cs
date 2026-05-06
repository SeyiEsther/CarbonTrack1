using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.Text;

namespace CarbonTrack.Controllers
{
    public class ReportsController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(CarbonTrackContext context, ILogger<ReportsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (TempData["Success"] is string success) ViewBag.Success = success;
            if (TempData["Error"]   is string error)   ViewBag.Error   = error;

            try
            {
                var trips = await _context.Trips.OrderByDescending(t => t.TripDate).ToListAsync();

                ViewBag.TotalTrips  = trips.Count;
                ViewBag.TotalTCO2e  = Math.Round(trips.Sum(t => t.KgCO2e) / 1000, 3);
                ViewBag.TotalKm     = Math.Round(trips.Sum(t => t.DistanceKm), 0);

                ViewBag.FlightTCO2e = Math.Round(trips.Where(t => t.TransportMode.StartsWith("Flight")).Sum(t => t.KgCO2e) / 1000, 3);
                ViewBag.TrainTCO2e  = Math.Round(trips.Where(t => t.TransportMode.StartsWith("Train")).Sum(t => t.KgCO2e) / 1000, 3);
                ViewBag.CarTCO2e    = Math.Round(trips.Where(t => t.TransportMode.StartsWith("Car") || t.TransportMode == "Taxi" || t.TransportMode.StartsWith("Ferry")).Sum(t => t.KgCO2e) / 1000, 3);

                // Mode breakdown grouped
                var modeBreakdown = trips
                    .GroupBy(t => t.TransportMode)
                    .Select(g => new ModeRow
                    {
                        Mode      = g.Key,
                        Count     = g.Count(),
                        TotalKm   = Math.Round(g.Sum(t => t.DistanceKm), 1),
                        TotalKg   = Math.Round(g.Sum(t => t.KgCO2e), 2),
                        TotalT    = Math.Round(g.Sum(t => t.KgCO2e) / 1000, 4),
                        AvgFactor = g.First().EmissionFactor
                    })
                    .OrderByDescending(x => x.TotalKg)
                    .ToList();

                ViewBag.ModeBreakdown = modeBreakdown;

                // Top 10 routes by emissions
                var topRoutes = trips
                    .GroupBy(t => $"{t.Origin} → {t.Destination}")
                    .Select(g => new RouteRow
                    {
                        Route    = g.Key,
                        Count    = g.Count(),
                        TotalKg  = Math.Round(g.Sum(t => t.KgCO2e), 2),
                        TotalT   = Math.Round(g.Sum(t => t.KgCO2e) / 1000, 3)
                    })
                    .OrderByDescending(x => x.TotalKg)
                    .Take(10)
                    .ToList();

                ViewBag.TopRoutes = topRoutes;

                return View(trips);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reports page load failed");
                ViewBag.TotalTrips = 0; ViewBag.TotalTCO2e = 0.0; ViewBag.TotalKm = 0.0;
                ViewBag.FlightTCO2e = 0.0; ViewBag.TrainTCO2e = 0.0; ViewBag.CarTCO2e = 0.0;
                ViewBag.ModeBreakdown = new List<ModeRow>();
                ViewBag.TopRoutes     = new List<RouteRow>();
                ViewBag.Error = "Could not load report data.";
                return View(new List<Trip>());
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            try
            {
                var trips = await _context.Trips.OrderByDescending(t => t.TripDate).ToListAsync();
                var sb = new StringBuilder();
                sb.AppendLine("Date,Origin,Destination,Transport Mode,Passengers,Distance (km),EF (kgCO2e/km),kgCO2e,Formula,Methodology,DEFRA Year,Logged (UTC)");

                foreach (var t in trips)
                    sb.AppendLine($"{t.TripDate:dd/MM/yyyy},{Esc(t.Origin)},{Esc(t.Destination)},{Esc(t.TransportMode)},{t.Passengers},{t.DistanceKm},{t.EmissionFactor},{t.KgCO2e},{Esc(t.Formula ?? "")},{Esc(t.DistanceMethodology ?? "")},{Esc(t.DefraFactorYear ?? "")},{t.CreatedAt:dd/MM/yyyy HH:mm}");

                _logger.LogInformation("CSV exported: {Count} trips", trips.Count);
                return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv",
                    $"CarbonTrack-Export-{DateTime.UtcNow:yyyy-MM-dd}.csv");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV export failed");
                TempData["Error"] = "CSV export failed. Please try again.";
                return RedirectToAction("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel()
        {
            try
            {
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
                var trips = await _context.Trips.OrderByDescending(t => t.TripDate).ToListAsync();

                using var pkg = new ExcelPackage();

                // ── Sheet 1: Audit Log ───────────────────────────────────────
                var ws = pkg.Workbook.Worksheets.Add("Audit Log");

                var headers = new[] { "Date", "Origin", "Destination", "Transport Mode",
                    "Passengers", "Distance (km)", "EF (kgCO₂e/km)", "kgCO₂e",
                    "Formula", "Methodology", "DEFRA Year", "Logged (UTC)" };

                for (int c = 0; c < headers.Length; c++)
                {
                    var cell = ws.Cells[1, c + 1];
                    cell.Value = headers[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(26, 42, 26));
                }

                for (int i = 0; i < trips.Count; i++)
                {
                    var t = trips[i]; int r = i + 2;
                    ws.Cells[r, 1].Value  = t.TripDate.ToString("dd/MM/yyyy");
                    ws.Cells[r, 2].Value  = t.Origin;
                    ws.Cells[r, 3].Value  = t.Destination;
                    ws.Cells[r, 4].Value  = t.TransportMode;
                    ws.Cells[r, 5].Value  = t.Passengers;
                    ws.Cells[r, 6].Value  = t.DistanceKm;
                    ws.Cells[r, 7].Value  = t.EmissionFactor;
                    ws.Cells[r, 8].Value  = t.KgCO2e;
                    ws.Cells[r, 9].Value  = t.Formula;
                    ws.Cells[r, 10].Value = t.DistanceMethodology;
                    ws.Cells[r, 11].Value = t.DefraFactorYear;
                    ws.Cells[r, 12].Value = t.CreatedAt.ToString("dd/MM/yyyy HH:mm");

                    if (i % 2 == 1)
                    {
                        ws.Cells[r, 1, r, headers.Length].Style.Fill.PatternType = ExcelFillStyle.Solid;
                        ws.Cells[r, 1, r, headers.Length].Style.Fill.BackgroundColor.SetColor(Color.FromArgb(20, 32, 20));
                    }
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                ws.View.FreezePanes(2, 1);

                // ── Sheet 2: Mode Summary ────────────────────────────────────
                var ws2 = pkg.Workbook.Worksheets.Add("Mode Summary");
                var sumHeaders = new[] { "Transport Mode", "Trips", "Total km", "Total kgCO₂e", "Total tCO₂e", "EF (kgCO₂e/km)" };

                for (int c = 0; c < sumHeaders.Length; c++)
                {
                    var cell = ws2.Cells[1, c + 1];
                    cell.Value = sumHeaders[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Font.Color.SetColor(Color.White);
                    cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    cell.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(26, 42, 26));
                }

                var modes = trips.GroupBy(t => t.TransportMode).OrderByDescending(g => g.Sum(t => t.KgCO2e)).ToList();
                for (int i = 0; i < modes.Count; i++)
                {
                    var g = modes[i]; int r = i + 2;
                    ws2.Cells[r, 1].Value = g.Key;
                    ws2.Cells[r, 2].Value = g.Count();
                    ws2.Cells[r, 3].Value = Math.Round(g.Sum(t => t.DistanceKm), 1);
                    ws2.Cells[r, 4].Value = Math.Round(g.Sum(t => t.KgCO2e), 2);
                    ws2.Cells[r, 5].Value = Math.Round(g.Sum(t => t.KgCO2e) / 1000, 4);
                    ws2.Cells[r, 6].Value = g.First().EmissionFactor;
                }
                ws2.Cells[ws2.Dimension?.Address ?? "A1"].AutoFitColumns();

                _logger.LogInformation("Excel exported: {Count} trips", trips.Count);
                return File(pkg.GetAsByteArray(),
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                    $"CarbonTrack-Audit-{DateTime.UtcNow:yyyy-MM-dd}.xlsx");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Excel export failed");
                TempData["Error"] = "Excel export failed. Please try again.";
                return RedirectToAction("Index");
            }
        }

        private static string Esc(string v) => v.Contains(',') || v.Contains('"') ? $"\"{v.Replace("\"", "\"\"")}\"" : v;
    }

    public class ModeRow
    {
        public string Mode { get; set; } = "";
        public int    Count     { get; set; }
        public double TotalKm   { get; set; }
        public double TotalKg   { get; set; }
        public double TotalT    { get; set; }
        public double AvgFactor { get; set; }
    }

    public class RouteRow
    {
        public string Route   { get; set; } = "";
        public int    Count   { get; set; }
        public double TotalKg { get; set; }
        public double TotalT  { get; set; }
    }
}
