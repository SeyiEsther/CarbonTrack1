using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CarbonTrack.Models
{
    public class Trip
    {
        public int Id { get; set; }

        [Required]
        public string Origin { get; set; }

        [Required]
        public string Destination { get; set; }

        [Required]
        public DateTime TripDate { get; set; }

        [Required]
        public string TransportMode { get; set; }

        public string? TravelClass { get; set; }

        public int Passengers { get; set; } = 1;

        public double DistanceKm { get; set; }

        public double EmissionFactor { get; set; }

        public double KgCO2e { get; set; }

        public string? DistanceMethodology { get; set; }

        public string DefraFactorYear { get; set; } = "DEFRA 2025";

        public string? Formula { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int OrganisationId { get; set; }

        [ForeignKey("OrganisationId")]
        public Organisation? Organisation { get; set; }
    }
}