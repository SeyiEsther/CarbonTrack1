using Newtonsoft.Json.Linq;

namespace CarbonTrack.Models
{
    public class GoogleMapsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<GoogleMapsService> _logger;

        public string ApiKey => _apiKey;

        public GoogleMapsService(HttpClient httpClient, IConfiguration configuration, ILogger<GoogleMapsService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["GoogleMaps:ApiKey"] ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_apiKey))
                _logger.LogWarning("Google Maps API key is not configured — distance calculations will fail");
        }

        public async Task<double> GetRoadDistanceKm(string origin, string destination)
        {
            if (string.IsNullOrWhiteSpace(origin)) throw new ArgumentException("Origin is required.", nameof(origin));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Destination is required.", nameof(destination));
            if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("Google Maps API key is not configured.");

            string url = "https://maps.googleapis.com/maps/api/distancematrix/json" +
                         $"?origins={Uri.EscapeDataString(origin)}" +
                         $"&destinations={Uri.EscapeDataString(destination)}" +
                         "&units=metric" +
                         $"&key={_apiKey}";

            string responseText;
            try
            {
                responseText = await _httpClient.GetStringAsync(url);
            }
            catch (TaskCanceledException)
            {
                throw new Exception("Google Maps request timed out. Please try again.");
            }

            var json = JObject.Parse(responseText);

            var topStatus = json["status"]?.ToString();
            if (topStatus == "REQUEST_DENIED")
                throw new Exception("Google Maps API key was denied. Check the key is valid and has Distance Matrix enabled.");
            if (topStatus != "OK")
                throw new Exception($"Google Maps API error: {topStatus}");

            var elementStatus = json["rows"]?[0]?["elements"]?[0]?["status"]?.ToString();
            if (elementStatus == "ZERO_RESULTS")
                throw new Exception($"No route found between '{origin}' and '{destination}'. Try more specific place names.");
            if (elementStatus != "OK")
                throw new Exception($"Route calculation failed: {elementStatus}");

            var distanceValue = json["rows"]?[0]?["elements"]?[0]?["distance"]?["value"];
            if (distanceValue == null)
                throw new Exception("Google Maps returned no distance value. Check origin and destination.");

            var km = Math.Round(distanceValue.Value<double>() / 1000, 2);
            _logger.LogInformation("Road distance {Origin}→{Destination}: {Km} km", origin, destination, km);
            return km;
        }

        public async Task<double> GetFlightDistanceKm(string origin, string destination)
        {
            if (string.IsNullOrWhiteSpace(origin)) throw new ArgumentException("Origin is required.", nameof(origin));
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Destination is required.", nameof(destination));

            var originCoords = await GetCoordinates(origin);
            var destCoords = await GetCoordinates(destination);

            var km = DefraCalculator.HaversineDistance(originCoords.lat, originCoords.lon, destCoords.lat, destCoords.lon);
            _logger.LogInformation("Flight distance {Origin}→{Destination}: {Km} km (Haversine)", origin, destination, km);
            return km;
        }

        private async Task<(double lat, double lon)> GetCoordinates(string location)
        {
            if (string.IsNullOrWhiteSpace(_apiKey)) throw new InvalidOperationException("Google Maps API key is not configured.");

            string url = "https://maps.googleapis.com/maps/api/geocode/json" +
                         $"?address={Uri.EscapeDataString(location)}" +
                         $"&key={_apiKey}";

            string responseText;
            try
            {
                responseText = await _httpClient.GetStringAsync(url);
            }
            catch (TaskCanceledException)
            {
                throw new Exception($"Geocoding timed out for '{location}'. Please try again.");
            }

            var json = JObject.Parse(responseText);

            var status = json["status"]?.ToString();
            if (status == "REQUEST_DENIED")
                throw new Exception("Google Maps API key was denied. Check the key is valid and has Geocoding enabled.");
            if (status == "ZERO_RESULTS" || json["results"] == null || !json["results"]!.Any())
                throw new Exception($"Location not found: '{location}'. Use a city name or IATA airport code.");

            var lat = json["results"]?[0]?["geometry"]?["location"]?["lat"]?.Value<double>();
            var lon = json["results"]?[0]?["geometry"]?["location"]?["lng"]?.Value<double>();

            if (lat == null || lon == null)
                throw new Exception($"Could not read coordinates for '{location}'.");

            return (lat.Value, lon.Value);
        }

        public async Task<double> GetDistanceKm(string origin, string destination, string transportMode)
        {
            if (string.IsNullOrWhiteSpace(origin)) throw new ArgumentException("Origin is required.");
            if (string.IsNullOrWhiteSpace(destination)) throw new ArgumentException("Destination is required.");
            if (string.IsNullOrWhiteSpace(transportMode)) throw new ArgumentException("Transport mode is required.");

            return transportMode.StartsWith("Flight")
                ? await GetFlightDistanceKm(origin, destination)
                : await GetRoadDistanceKm(origin, destination);
        }
    }
}
