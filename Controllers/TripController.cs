using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
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

        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        // Represents one leg of a journey; serialised into Trip.Waypoints
        private sealed class TripLeg
        {
            public string  From           { get; set; } = "";
            public string  To             { get; set; } = "";
            public string  Mode           { get; set; } = "";
            public string? Class          { get; set; }
            public double  DistanceKm     { get; set; }
            public double  EmissionFactor { get; set; }
            public double  KgCO2e         { get; set; }
            public string  Formula        { get; set; } = "";
        }

        public TripController(
            CarbonTrackContext context,
            GoogleMapsService mapsService,
            UserManager<ApplicationUser> userManager,
            ILogger<TripController> logger)
        {
            _context     = context;
            _mapsService = mapsService;
            _userManager = userManager;
            _logger      = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (TempData["Success"] is string success) ViewBag.Success = success;
            if (TempData["Error"]   is string error)   ViewBag.Error   = error;

            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                var orgId       = currentUser?.OrganisationId ?? 0;
                var isEmployee  = User.IsInRole("Employee");
                var userId      = currentUser?.Id;

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
            // All of these are computed server-side; remove from ModelState so [Required] etc. don't fire
            ModelState.Remove(nameof(Trip.TransportMode));
            ModelState.Remove(nameof(Trip.Waypoints));
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
                    _logger.LogWarning("ModelState error: {Message}", e.ErrorMessage);
                ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                return View(trip);
            }

            try
            {
                // ── Parse legs from submitted JSON ────────────────────────────────────
                List<TripLeg> legs;
                try
                {
                    legs = !string.IsNullOrWhiteSpace(trip.Waypoints)
                        ? JsonSerializer.Deserialize<List<TripLeg>>(trip.Waypoints, _json) ?? []
                        : [];
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Invalid waypoints JSON submitted");
                    ViewBag.Error = "Route data was malformed. Please reload the page and re-enter your route.";
                    ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                    return View(trip);
                }

                if (legs.Count == 0)
                {
                    ViewBag.Error = "Route data is missing. Please ensure Origin and Destination are filled.";
                    ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                    return View(trip);
                }

                // ── Validate every leg ────────────────────────────────────────────────
                for (int i = 0; i < legs.Count; i++)
                {
                    var leg = legs[i];
                    if (string.IsNullOrWhiteSpace(leg.From) || string.IsNullOrWhiteSpace(leg.To))
                    {
                        ViewBag.Error = $"Leg {i + 1}: both origin and destination are required — check your stopover fields.";
                        ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                        return View(trip);
                    }
                    if (string.IsNullOrWhiteSpace(leg.Mode))
                    {
                        ViewBag.Error = $"Leg {i + 1} ({leg.From} → {leg.To}): please select a transport mode.";
                        ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                        return View(trip);
                    }
                    if (DefraCalculator.GetEmissionFactor(leg.Mode, null) <= 0)
                    {
                        ViewBag.Error = $"Leg {i + 1}: unrecognised transport mode '{leg.Mode}'.";
                        ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                        return View(trip);
                    }
                }

                // Sync Origin/Destination from legs so they can't be tampered with independently
                trip.Origin      = legs[0].From;
                trip.Destination = legs[^1].To;

                // ── Calculate each leg independently ──────────────────────────────────
                double totalKm = 0, totalKgCO2e = 0;

                foreach (var leg in legs)
                {
                    leg.EmissionFactor = DefraCalculator.GetEmissionFactor(leg.Mode, null);

                    // Extract human-readable class from mode key, e.g. "Flight-LongHaul-Business" → "Business"
                    var parts = leg.Mode.Split('-');
                    leg.Class = parts.Length >= 3 ? parts[2] : null;

                    leg.DistanceKm = await _mapsService.GetDistanceKm(leg.From, leg.To, leg.Mode);
                    if (leg.DistanceKm <= 0)
                    {
                        ViewBag.Error = $"No distance returned for {leg.From} → {leg.To}. Check the place names.";
                        ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
                        return View(trip);
                    }

                    leg.KgCO2e = DefraCalculator.CalculateKgCO2e(
                        leg.DistanceKm, leg.EmissionFactor, trip.Passengers, leg.Mode);
                    leg.Formula = DefraCalculator.GetFormula(
                        leg.DistanceKm, leg.EmissionFactor, trip.Passengers, leg.Mode);

                    totalKm     += leg.DistanceKm;
                    totalKgCO2e += leg.KgCO2e;
                }

                // ── Dominant mode = leg covering the most distance ────────────────────
                trip.TransportMode = legs.OrderByDescending(l => l.DistanceKm).First().Mode;

                // ── Full audit formula ────────────────────────────────────────────────
                trip.Formula = legs.Count == 1
                    ? $"{legs[0].Formula} = {legs[0].KgCO2e:F2} kgCO₂e"
                    : string.Join(" | ", legs.Select((l, i) =>
                          $"Leg {i + 1} ({l.From}→{l.To}): {l.Formula} = {l.KgCO2e:F2} kgCO₂e"))
                      + $" | Total: {totalKgCO2e:F2} kgCO₂e";

                trip.DistanceKm    = Math.Round(totalKm, 2);
                trip.KgCO2e        = Math.Round(totalKgCO2e, 2);
                // Single EF only meaningful for single-leg trips; 0 flags composite trips
                trip.EmissionFactor = legs.Count == 1 ? legs[0].EmissionFactor : 0;

                trip.DistanceMethodology = legs.Count == 1
                    ? DefraCalculator.GetDistanceMethodology(legs[0].Mode)
                    : string.Join(" + ", legs
                          .Select(l => DefraCalculator.GetDistanceMethodology(l.Mode))
                          .Distinct());

                // Store enriched legs JSON for multi-leg trips; null keeps single-leg rows lean
                trip.Waypoints = legs.Count > 1
                    ? JsonSerializer.Serialize(legs, _json)
                    : null;

                var currentUser      = await _userManager.GetUserAsync(User);
                trip.DefraFactorYear = "DEFRA 2025";
                trip.OrganisationId  = currentUser?.OrganisationId ?? 0;
                trip.UserId          = currentUser?.Id;
                trip.CreatedAt       = DateTime.UtcNow;

                _context.Trips.Add(trip);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Trip saved: {Route} | {Legs} leg(s) | {Km} km | {Kg} kgCO2e",
                    trip.RouteDescription, legs.Count, trip.DistanceKm, trip.KgCO2e);

                TempData["Success"] = $"Trip logged: {trip.RouteDescription} ({trip.KgCO2e:F2} kgCO₂e)";
                return RedirectToAction("Index");
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Google Maps HTTP error for route {Route}", trip.RouteDescription);
                ViewBag.Error = "Could not reach Google Maps. Check your internet connection or API key and try again.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving trip {Route}", trip.RouteDescription);
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

                _logger.LogInformation("Trip {Id} deleted: {Route}", id, trip.RouteDescription);
                TempData["Success"] = $"Deleted: {trip.RouteDescription}";
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
