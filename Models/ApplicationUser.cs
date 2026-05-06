using Microsoft.AspNetCore.Identity;

namespace CarbonTrack.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string FullName { get; set; } = "";
        public int? OrganisationId { get; set; }
        public Organisation? Organisation { get; set; }
    }
}
