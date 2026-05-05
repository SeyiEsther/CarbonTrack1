using Newtonsoft.Json.Linq;

namespace CarbonTrack.Models
{
    public class GoogleMapsService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;

        public string ApiKey => _apiKey; // ← added

        public GoogleMapsService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _apiKey = configuration["GoogleMaps:ApiKey"];
        }

        public async Task<double> GetRoadDistanceKm(string origin, string destination)
        {
            string url = $"https://maps.googleapis.com/maps/api/distancematrix/json" +
                         $"?origins={Uri.EscapeDataString(origin)}" +
                         $"&destinations={Uri.EscapeDataString(destination)}" +
                         $"&units=metric" +
                         $"&key={_apiKey}";

            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);

            var distanceValue = json["rows"]?[0]?["elements"]?[0]?["distance"]?["value"];

            if (distanceValue == null)
                throw new Exception("Could not calculate road distance. Check origin and destination.");

            return Math.Round(distanceValue.Value<double>() / 1000, 2);
        }

        public async Task<double> GetFlightDistanceKm(string origin, string destination)
        {
            var originCoords = await GetCoordinates(origin);
            var destCoords = await GetCoordinates(destination);

            return DefraCalculator.HaversineDistance(
                originCoords.lat, originCoords.lon,
                destCoords.lat, destCoords.lon);
        }

        private async Task<(double lat, double lon)> GetCoordinates(string location)
        {
            string url = $"https://maps.googleapis.com/maps/api/geocode/json" +
                         $"?address={Uri.EscapeDataString(location)}" +
                         $"&key={_apiKey}";

            var response = await _httpClient.GetStringAsync(url);
            var json = JObject.Parse(response);

            var lat = json["results"]?[0]?["geometry"]?["location"]?["lat"]?.Value<double>();
            var lon = json["results"]?[0]?["geometry"]?["location"]?["lng"]?.Value<double>();

            if (lat == null || lon == null)
                throw new Exception($"Could not find coordinates for: {location}");

            return (lat.Value, lon.Value);
        }

        public async Task<double> GetDistanceKm(string origin, string destination, string transportMode)
        {
            if (transportMode.StartsWith("Flight"))
                return await GetFlightDistanceKm(origin, destination);
            else
                return await GetRoadDistanceKm(origin, destination);
        }
    }
}