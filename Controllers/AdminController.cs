using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    public class AdminController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<AdminController> _logger;

        public AdminController(CarbonTrackContext context, ILogger<AdminController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET /Admin — data management panel
        public async Task<IActionResult> Index()
        {
            ViewBag.TripCount = await _context.Trips.CountAsync();
            ViewBag.OrgCount  = await _context.Organisations.CountAsync();
            return View();
        }

        // POST /Admin/ClearTrips — delete ALL trip records
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ClearTrips(string confirm)
        {
            if (confirm != "DELETE ALL TRIPS")
            {
                TempData["Error"] = "Type confirmation text exactly to proceed.";
                return RedirectToAction("Index");
            }

            try
            {
                int count = await _context.Trips.CountAsync();
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Trips");
                _logger.LogWarning("All trips deleted by admin — {Count} records removed", count);
                TempData["Success"] = $"Cleared {count} trip records. Database is now empty.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ClearTrips failed");
                TempData["Error"] = "Clear failed. Please try again.";
            }

            return RedirectToAction("Index");
        }

        // POST /Admin/ResetOnboarding — clear profile so onboarding runs again
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetOnboarding()
        {
            try
            {
                var org = await _context.Organisations.FirstOrDefaultAsync(o => o.Id == 1);
                if (org != null)
                {
                    org.UserType           = null;
                    org.Sector             = null;
                    org.ComplianceFlags    = null;
                    org.OnboardingComplete = false;
                    await _context.SaveChangesAsync();
                }
                TempData["Success"] = "Onboarding reset. You'll see the welcome screen on next Dashboard visit.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResetOnboarding failed");
                TempData["Error"] = "Reset failed.";
            }

            return RedirectToAction("Index");
        }
    }
}
