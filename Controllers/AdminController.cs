using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<AdminController> _logger;

        public AdminController(CarbonTrackContext context, UserManager<ApplicationUser> userManager, ILogger<AdminController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger  = logger;
        }

        // GET /Admin — data management panel
        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var orgId = currentUser?.OrganisationId ?? 0;
            ViewBag.TripCount = await _context.Trips.CountAsync(t => t.OrganisationId == orgId);
            ViewBag.OrgCount  = await _context.Organisations.CountAsync();
            return View();
        }

        // POST /Admin/ClearTrips — delete trip records for this org
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
                var currentUser = await _userManager.GetUserAsync(User);
                var orgId = currentUser?.OrganisationId ?? 0;
                int count = await _context.Trips.CountAsync(t => t.OrganisationId == orgId);
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM Trips WHERE OrganisationId = {0}", orgId);
                _logger.LogWarning("All trips deleted by admin for org {OrgId} — {Count} records removed", orgId, count);
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
                var currentUser = await _userManager.GetUserAsync(User);
                var orgId = currentUser?.OrganisationId ?? 0;
                var org = await _context.Organisations.FindAsync(orgId);
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
