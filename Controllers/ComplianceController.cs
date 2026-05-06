using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    public class ComplianceController : Controller
    {
        private readonly CarbonTrackContext _context;

        public ComplianceController(CarbonTrackContext context) => _context = context;

        public async Task<IActionResult> Index()
        {
            var org   = await _context.Organisations.FirstOrDefaultAsync(o => o.Id == 1);
            var trips = await _context.Trips.ToListAsync();

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
