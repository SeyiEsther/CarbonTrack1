using System.ComponentModel.DataAnnotations;

namespace CarbonTrack.Models
{
    public class Organisation
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; }

        public string ReportingPeriodStart { get; set; }

        public string ReportingPeriodEnd { get; set; }

        public string ContactEmail { get; set; }

        public string Plan { get; set; } = "Free Trial";

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public ICollection<Trip> Trips { get; set; }
    }
}