using Microsoft.EntityFrameworkCore;

namespace CarbonTrack.Models
{
    public class CarbonTrackContext : DbContext
    {
        public CarbonTrackContext(DbContextOptions<CarbonTrackContext> options)
            : base(options)
        {
        }

        public DbSet<Trip> Trips { get; set; }
        public DbSet<Organisation> Organisations { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }
    }
}