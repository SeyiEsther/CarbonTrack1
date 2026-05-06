namespace CarbonTrack.Models
{
    public class Invite
    {
        public int Id { get; set; }
        public string Email { get; set; } = "";
        public string Token { get; set; } = "";
        public string Role { get; set; } = "Employee";
        public int OrganisationId { get; set; }
        public Organisation? Organisation { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(7);
        public bool Used { get; set; }
    }
}
