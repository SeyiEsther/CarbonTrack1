using System.ComponentModel.DataAnnotations;

namespace CarbonTrack.Models
{
    public class Organisation
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = "";

        public string ReportingPeriodStart { get; set; } = "";
        public string ReportingPeriodEnd   { get; set; } = "";
        public string ContactEmail         { get; set; } = "";
        public string Plan                 { get; set; } = "Free Trial";
        public DateTime CreatedAt          { get; set; } = DateTime.UtcNow;

        // ── Onboarding profile ─────────────────────────────────────────────────
        // UserType: "Consultant" | "Corporate" | "SME" | "Government"
        public string? UserType  { get; set; }

        // Sector: "Construction" | "Professional Services" | "Engineering" |
        //         "Technology" | "Legal" | "Architecture" | "Finance" | "Other"
        public string? Sector    { get; set; }

        // Compliance frameworks selected during onboarding — comma-separated
        // Values: SECR, PPN0621, TCFD, GHGProtocol, ESOS, CSRD
        public string? ComplianceFlags { get; set; }

        // Intensity metric denominators
        public int?     Employees     { get; set; }
        public decimal? TurnoverGBPm  { get; set; }  // £ millions

        public bool OnboardingComplete { get; set; } = false;

        public ICollection<Trip> Trips { get; set; } = new List<Trip>();

        // ── Helpers ────────────────────────────────────────────────────────────
        public IReadOnlyList<string> GetComplianceFlags() =>
            string.IsNullOrWhiteSpace(ComplianceFlags)
                ? Array.Empty<string>()
                : ComplianceFlags.Split(',', StringSplitOptions.RemoveEmptyEntries);

        public bool HasFlag(string flag) =>
            GetComplianceFlags().Contains(flag, StringComparer.OrdinalIgnoreCase);

        public string UserTypeLabel => UserType switch
        {
            "Consultant"  => "Sustainability Consultant",
            "Corporate"   => "Corporate (In-house)",
            "SME"         => "Small / Medium Enterprise",
            "Government"  => "Government / Public Sector",
            _             => "Organisation",
        };
    }
}
