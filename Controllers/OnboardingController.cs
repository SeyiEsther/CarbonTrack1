using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    public class OnboardingController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<OnboardingController> _logger;

        public OnboardingController(CarbonTrackContext context, ILogger<OnboardingController> logger)
        {
            _context = context;
            _logger  = logger;
        }

        // GET /Onboarding
        public async Task<IActionResult> Index()
        {
            var org = await _context.Organisations.FirstOrDefaultAsync(o => o.Id == 1);
            if (org == null) return RedirectToAction("Index", "Dashboard");
            return View(org);
        }

        // POST /Onboarding/Save
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            string userType,
            string sector,
            string? orgName,
            string? contactEmail,
            int? employees,
            decimal? turnoverGBPm,
            string? complianceFlags)   // comma-joined from checkboxes
        {
            try
            {
                var org = await _context.Organisations.FirstOrDefaultAsync(o => o.Id == 1);
                if (org == null) return RedirectToAction("Index", "Dashboard");

                if (!string.IsNullOrWhiteSpace(orgName))
                    org.Name = orgName.Trim();

                if (!string.IsNullOrWhiteSpace(contactEmail))
                    org.ContactEmail = contactEmail.Trim();

                org.UserType           = userType;
                org.Sector             = sector;
                org.ComplianceFlags    = complianceFlags ?? "";
                org.Employees          = employees;
                org.TurnoverGBPm       = turnoverGBPm;
                org.OnboardingComplete = true;

                await _context.SaveChangesAsync();
                _logger.LogInformation("Onboarding saved: {UserType} / {Sector}", userType, sector);

                TempData["Success"] = "Profile saved — your dashboard is now personalised.";
                return RedirectToAction("Index", "Dashboard");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Onboarding save failed");
                TempData["Error"] = "Could not save profile. Please try again.";
                return RedirectToAction("Index");
            }
        }
    }
}
