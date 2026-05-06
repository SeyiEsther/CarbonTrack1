using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    [Authorize(Roles = "Admin,Consultant")]
    public class ComplianceController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ComplianceController(CarbonTrackContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var currentUser = await _userManager.GetUserAsync(User);
            var orgId = currentUser?.OrganisationId ?? 0;
            var org   = orgId > 0 ? await _context.Organisations.FindAsync(orgId) : null;
            var trips = await _context.Trips.Where(t => t.OrganisationId == orgId).ToListAsync();

            ViewBag.Org           = org;
            ViewBag.TripCount     = trips.Count;
            ViewBag.TotalTCO2e    = Math.Round(trips.Sum(t => t.KgCO2e) / 1000, 4);
            ViewBag.HasSecr       = org?.HasFlag("SECR")       ?? false;
            ViewBag.HasPpn        = org?.HasFlag("PPN0621")    ?? false;
            ViewBag.HasTcfd       = org?.HasFlag("TCFD")       ?? false;
            ViewBag.HasGhg        = org?.HasFlag("GHGProtocol") ?? false;
            ViewBag.HasEsos       = org?.HasFlag("ESOS")       ?? false;
            ViewBag.HasCsrd       = org?.HasFlag("CSRD")       ?? false;

            return View();
        }
    }
}
