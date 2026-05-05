using Microsoft.AspNetCore.Mvc;
using CarbonTrack.Models;

namespace CarbonTrack.Controllers
{
    public class TripController : Controller
    {
        private readonly CarbonTrackContext _context;
        private readonly GoogleMapsService _mapsService;

        public TripController(CarbonTrackContext context, GoogleMapsService mapsService)
        {
            _context = context;
            _mapsService = mapsService;
        }

        public IActionResult Index()
        {
            var trips = _context.Trips.ToList();
            return View(trips);
        }

        public IActionResult LogTrip() // ← updated
        {
            ViewBag.GoogleMapsApiKey = _mapsService.ApiKey;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> LogTrip(Trip trip)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    trip.DistanceKm = await _mapsService.GetDistanceKm(
                        trip.Origin,
                        trip.Destination,
                        trip.TransportMode);

                    trip.EmissionFactor = DefraCalculator.GetEmissionFactor(
                        trip.TransportMode,
                        trip.TravelClass);

                    trip.KgCO2e = DefraCalculator.CalculateKgCO2e(
                        trip.DistanceKm,
                        trip.EmissionFactor,
                        trip.Passengers);

                    trip.Formula = DefraCalculator.GetFormula(
                        trip.DistanceKm,
                        trip.EmissionFactor,
                        trip.Passengers);

                    trip.DistanceMethodology = DefraCalculator.GetDistanceMethodology(
                        trip.TransportMode);

                    trip.DefraFactorYear = "DEFRA 2025";
                    trip.OrganisationId = 1;

                    _context.Trips.Add(trip);
                    await _context.SaveChangesAsync();

                    return RedirectToAction("Index");
                }

                foreach (var error in ModelState.Values
                    .SelectMany(v => v.Errors))
                {
                    Console.WriteLine("Validation error: " + error.ErrorMessage);
                }

                return View(trip);
            }
            catch (Exception ex)
            {
                var innerMessage = ex.InnerException?.Message ?? ex.Message;
                Console.WriteLine("ERROR: " + innerMessage);
                ViewBag.Error = innerMessage;
                ViewBag.GoogleMapsApiKey = _mapsService.ApiKey; // ← also needed on error re-render
                return View(trip);
            }
        }
    }
}