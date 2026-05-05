using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    public class DashboardController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(CarbonTrackContext context, ILogger<DashboardController> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (TempData["Success"] is string success) ViewBag.Success = success;
            if (TempData["Error"] is string tempError) ViewBag.Error = tempError;

            try
            {
                var trips = await _context.Trips.ToListAsync();

                ViewBag.TotalTrips = trips.Count;
                ViewBag.TotalTCO2e = Math.Round(trips.Sum(t => t.KgCO2e) / 1000, 2);
                ViewBag.FlightEmissions = Math.Round(
                    trips.Where(t => t.TransportMode.StartsWith("Flight")).Sum(t => t.KgCO2e) / 1000, 2);
                ViewBag.TrainEmissions = Math.Round(
                    trips.Where(t => t.TransportMode.StartsWith("Train")).Sum(t => t.KgCO2e) / 1000, 2);
                ViewBag.CarEmissions = Math.Round(
                    trips.Where(t => t.TransportMode.StartsWith("Car") || t.TransportMode == "Taxi" || t.TransportMode.StartsWith("Ferry")).Sum(t => t.KgCO2e) / 1000, 2);
                ViewBag.FlightCount = trips.Count(t => t.TransportMode.StartsWith("Flight"));
                ViewBag.TrainCount = trips.Count(t => t.TransportMode.StartsWith("Train"));
                ViewBag.CarCount = trips.Count(t => t.TransportMode.StartsWith("Car") || t.TransportMode == "Taxi" || t.TransportMode.StartsWith("Ferry"));
                ViewBag.RecentTrips = trips.OrderByDescending(t => t.TripDate).Take(5).ToList();

                // Monthly breakdown — last 12 months for bar chart
                var now = DateTime.UtcNow;
                var monthly = Enumerable.Range(0, 12)
                    .Select(i =>
                    {
                        var month = now.AddMonths(-11 + i);
                        var m = trips.Where(t => t.TripDate.Year == month.Year && t.TripDate.Month == month.Month);
                        return new
                        {
                            Label = month.ToString("MMM"),
                            Flights = Math.Round(m.Where(t => t.TransportMode.StartsWith("Flight")).Sum(t => t.KgCO2e) / 1000, 3),
                            Rail = Math.Round(m.Where(t => t.TransportMode.StartsWith("Train")).Sum(t => t.KgCO2e) / 1000, 3),
                            Car = Math.Round(m.Where(t => t.TransportMode.StartsWith("Car") || t.TransportMode == "Taxi" || t.TransportMode.StartsWith("Ferry")).Sum(t => t.KgCO2e) / 1000, 3),
                        };
                    })
                    .ToList();

                ViewBag.MonthlyData = System.Text.Json.JsonSerializer.Serialize(monthly);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Dashboard load failed");
                ViewBag.TotalTrips = 0;
                ViewBag.TotalTCO2e = 0.0;
                ViewBag.FlightEmissions = 0.0;
                ViewBag.TrainEmissions = 0.0;
                ViewBag.CarEmissions = 0.0;
                ViewBag.FlightCount = 0;
                ViewBag.TrainCount = 0;
                ViewBag.CarCount = 0;
                ViewBag.RecentTrips = new List<Trip>();
                ViewBag.MonthlyData = "[]";
                if (ViewBag.Error == null)
                    ViewBag.Error = "Could not load dashboard data — database may be unavailable.";
            }

            return View();
        }
    }
}
