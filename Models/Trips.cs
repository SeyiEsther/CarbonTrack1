using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CarbonTrack.Models
{
    public class Trip
    {
        public int Id { get; set; }

        [Required]
        public string Origin { get; set; } = "";

        [Required]
        public string Destination { get; set; } = "";

        // JSON array of intermediate stop names, e.g. ["Frankfurt","Dubai"]
        public string? Waypoints { get; set; }

        [NotMapped]
        public string RouteDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Waypoints)) return $"{Origin} → {Destination}";
                try
                {
                    using var doc  = System.Text.Json.JsonDocument.Parse(Waypoints);
                    var root       = doc.RootElement;
                    if (root.ValueKind != System.Text.Json.JsonValueKind.Array || root.GetArrayLength() == 0)
                        return $"{Origin} → {Destination}";

                    // Legs JSON: [{from, to, mode, ...}, ...]
                    var stops = new System.Collections.Generic.List<string>();
                    if (root[0].TryGetProperty("from", out var fromEl))
                        stops.Add(fromEl.GetString() ?? Origin);
                    foreach (var leg in root.EnumerateArray())
                        if (leg.TryGetProperty("to", out var toEl))
                            stops.Add(toEl.GetString() ?? "");
                    var result = string.Join(" → ", stops.Where(s => !string.IsNullOrEmpty(s)));
                    return string.IsNullOrEmpty(result) ? $"{Origin} → {Destination}" : result;
                }
                catch { return $"{Origin} → {Destination}"; }
            }
        }

        [Required]
        public DateTime TripDate { get; set; }

        [Required]
        public string TransportMode { get; set; } = "";

        public string? TravelClass { get; set; }

        [Range(1, 500, ErrorMessage = "Passengers must be between 1 and 500.")]
        public int Passengers { get; set; } = 1;

        public double DistanceKm { get; set; }

        public double EmissionFactor { get; set; }

        public double KgCO2e { get; set; }

        // Gas-component breakdown (populated for DESNZ-scheme uploads; null for manually-logged trips)
        public double? KgCO2  { get; set; }
        public double? KgCH4  { get; set; }
        public double? KgN2O  { get; set; }

        public string? DistanceMethodology { get; set; }

        public string DefraFactorYear { get; set; } = "DEFRA 2025";

        public string? Formula { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public int OrganisationId { get; set; }

        [ForeignKey("OrganisationId")]
        public Organisation? Organisation { get; set; }

        // Nullable: populated for new trips logged by authenticated users;
        // null for trips imported before auth was introduced.
        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }
    }
}