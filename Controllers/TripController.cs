using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    [Authorize]
    public class TripController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly GoogleMapsService _mapsService;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<TripController> _logger;

        public TripController(CarbonTrackContext context, GoogleMapsService mapsService, UserManager<ApplicationUser> userManager, ILogger<TripController> logger)
        {
            _context = context;
            _mapsService = mapsService;
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (TempData["Success"] is string success) ViewBag.Success = success;
            if (TempData["Error"] is string error) ViewBag.Error = error;

            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                var orgId = currentUser?.OrganisationId ?? 0;
                var isEmployee = User.IsInRole("Employee");
                var userId = currentUser?.Id;

                var trips = isEmployee && userId != null
                    ? await _context.Trips.Where(t => t.UserId == userId).OrderByDescending(t => t.TripDate).ToListAsync()
                    : await _context.Trips.Where(t => t.OrganisationId == orgId).OrderByDescending(t => t.TripDate).ToListAsync();

                return View(trips);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load trips");
                ViewBag.Error = "Could not load trip data. Please try again.";
                return View(new List<Trip>());
            }
        }

        public IActionResult LogTrip()
        {
            ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
            return View(new Trip { TripDate = DateTime.Today, Passengers = 1 });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LogTrip(Trip trip)
        {
            // Strip server-calculated fields from validation — they are not user inputs
            ModelState.Remove(nameof(Trip.DistanceKm));
            ModelState.Remove(nameof(Trip.EmissionFactor));
            ModelState.Remove(nameof(Trip.KgCO2e));
            ModelState.Remove(nameof(Trip.Formula));
            ModelState.Remove(nameof(Trip.DistanceMethodology));
            ModelState.Remove(nameof(Trip.DefraFactorYear));
            ModelState.Remove(nameof(Trip.CreatedAt));
            ModelState.Remove(nameof(Trip.OrganisationId));
            ModelState.Remove(nameof(Trip.UserId));

            if (!ModelState.IsValid)
            {
                foreach (var e in ModelState.Values.SelectMany(v => v.Errors))
                    _logger.LogWarning("Validation error: {Message}", e.ErrorMessage);
                ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                return View(trip);
            }

            try
            {
                trip.EmissionFactor = DefraCalculator.GetEmissionFactor(trip.TransportMode, trip.TravelClass);
                if (trip.EmissionFactor <= 0)
                {
                    ViewBag.Error = $"Unrecognised transport mode: {trip.TransportMode}";
                    ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                    return View(trip);
                }

                trip.DistanceKm = await _mapsService.GetDistanceKm(trip.Origin, trip.Destination, trip.TransportMode);
                if (trip.DistanceKm <= 0)
                {
                    ViewBag.Error = "Distance returned as zero — check origin and destination names.";
                    ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                    return View(trip);
                }

                trip.KgCO2e = DefraCalculator.CalculateKgCO2e(trip.DistanceKm, trip.EmissionFactor, trip.Passengers, trip.TransportMode);
                trip.Formula = DefraCalculator.GetFormula(trip.DistanceKm, trip.EmissionFactor, trip.Passengers, trip.TransportMode);
                trip.DistanceMethodology = DefraCalculator.GetDistanceMethodology(trip.TransportMode);
                var currentUser = await _userManager.GetUserAsync(User);
                trip.DefraFactorYear = "DEFRA 2025";
                trip.OrganisationId = currentUser?.OrganisationId ?? 0;
                trip.UserId = currentUser?.Id;
                trip.CreatedAt = DateTime.UtcNow;

                _context.Trips.Add(trip);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Trip saved: {Origin}→{Destination} | {Mode} | {Km}km | {Kg}kgCO2e",
                    trip.Origin, trip.Destination, trip.TransportMode, trip.DistanceKm, trip.KgCO2e);

                TempData["Success"] = $"Trip logged: {trip.Origin} → {trip.Destination} ({trip.KgCO2e} kgCO₂e)";
                return RedirectToAction("Index");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Google Maps HTTP error for {Origin}→{Destination}", trip.Origin, trip.Destination);
                ViewBag.Error = "Could not reach Google Maps. Check your internet connection or API key and try again.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving trip {Origin}→{Destination}", trip.Origin, trip.Destination);
                ViewBag.Error = ex.InnerException?.Message ?? ex.Message;
            }

            ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
            return View(trip);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var trip = await _context.Trips.FindAsync(id);
                if (trip == null)
                {
                    TempData["Error"] = "Trip not found.";
                    return RedirectToAction("Index");
                }

                _context.Trips.Remove(trip);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Trip {Id} deleted: {Origin}→{Destination}", id, trip.Origin, trip.Destination);
                TempData["Success"] = $"Deleted: {trip.Origin} → {trip.Destination}";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting trip {Id}", id);
                TempData["Error"] = "Failed to delete trip. Please try again.";
            }

            return RedirectToAction("Index");
        }
    }
}
