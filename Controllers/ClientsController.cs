using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    [Authorize(Roles = "Consultant")]
    public class ClientsController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly ILogger<ClientsController> _logger;

        public ClientsController(CarbonTrackContext context, ILogger<ClientsController> logger)
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
                var orgs  = await _context.Organisations.OrderBy(o => o.Name).ToListAsync();
                var trips = await _context.Trips.ToListAsync();

                var rows = orgs.Select(org => new ClientRow
                {
                    Org        = org,
                    TripCount  = trips.Count(t => t.OrganisationId == org.Id),
                    TotalTCO2e = Math.Round(trips.Where(t => t.OrganisationId == org.Id).Sum(t => t.KgCO2e) / 1000, 2),
                    LastTrip   = trips.Where(t => t.OrganisationId == org.Id)
                                      .OrderByDescending(t => t.TripDate)
                                      .FirstOrDefault()?.TripDate
                }).ToList();

                return View(rows);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clients page load failed");
                ViewBag.Error = "Could not load client data.";
                return View(new List<ClientRow>());
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(string name, string email)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                TempData["Error"] = "Organisation name is required.";
                return RedirectToAction("Index");
            }

            try
            {
                _context.Organisations.Add(new Organisation
                {
                    Name         = name.Trim(),
                    ContactEmail = (email ?? "").Trim(),
                    Plan         = "Free Trial",
                    CreatedAt    = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                _logger.LogInformation("Organisation added: {Name}", name);
                TempData["Success"] = $"Client '{name.Trim()}' added successfully.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add organisation {Name}", name);
                TempData["Error"] = "Failed to add client. Please try again.";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (id == 1)
            {
                TempData["Error"] = "The default organisation cannot be deleted.";
                return RedirectToAction("Index");
            }

            try
            {
                var org = await _context.Organisations.FindAsync(id);
                if (org == null)
                {
                    TempData["Error"] = "Client not found.";
                    return RedirectToAction("Index");
                }

                var hasTrips = await _context.Trips.AnyAsync(t => t.OrganisationId == id);
                if (hasTrips)
                {
                    TempData["Error"] = $"Cannot delete '{org.Name}' — it has trips logged against it.";
                    return RedirectToAction("Index");
                }

                _context.Organisations.Remove(org);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Organisation {Id} deleted: {Name}", id, org.Name);
                TempData["Success"] = $"Client '{org.Name}' deleted.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete organisation {Id}", id);
                TempData["Error"] = "Failed to delete client. Please try again.";
            }

            return RedirectToAction("Index");
        }
    }

    public class ClientRow
    {
        public Organisation Org        { get; set; } = null!;
        public int          TripCount  { get; set; }
        public double       TotalTCO2e { get; set; }
        public DateTime?    LastTrip   { get; set; }
    }
}
