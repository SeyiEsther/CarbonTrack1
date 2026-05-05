using Microsoft.AspNetCore.Mvc;
using CarbonTrack.Models;
using Microsoft.EntityFrameworkCore;

namespace CarbonTrack.Controllers
{
    public class DashboardController : Controller
    {
        private readonly CarbonTrackContext _context;

        public DashboardController(CarbonTrackContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            var trips = _context.Trips.ToList();

            ViewBag.TotalTrips = trips.Count;
            ViewBag.TotalKgCO2e = trips.Sum(t => t.KgCO2e);
            ViewBag.TotalTCO2e = Math.Round(trips.Sum(t => t.KgCO2e) / 1000, 2);
            ViewBag.FlightEmissions = Math.Round(trips.Where(t => t.TransportMode.StartsWith("Flight")).Sum(t => t.KgCO2e) / 1000, 2);
            ViewBag.RecentTrips = trips.OrderByDescending(t => t.TripDate).Take(5).ToList();

            return View();
        }
    }
}