using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CarbonTrack.Models
{
    public class CarbonTrackContext : IdentityDbContext<ApplicationUser>
    {
        public CarbonTrackContext(DbContextOptions<CarbonTrackContext> options)
            : base(options)
        {
        }

        public DbSet<Trip> Trips { get; set; }
        public DbSet<Organisation> Organisations { get; set; }
        public DbSet<Invite> Invites { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicit cascade rules to avoid SQL Server multiple-path errors.
            // Trips.OrganisationId already has CASCADE (from InitialCreate).
            // ApplicationUser.OrganisationId uses SET NULL so deleting an org
            // doesn't try to cascade-delete users AND cascade-delete trips simultaneously.
            modelBuilder.Entity<ApplicationUser>()
                .HasOne(u => u.Organisation)
                .WithMany()
                .HasForeignKey(u => u.OrganisationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Trips.UserId: set null when a user is deleted (trip data is preserved)
            modelBuilder.Entity<Trip>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Invites cascade with org
            modelBuilder.Entity<Invite>()
                .HasOne(i => i.Organisation)
                .WithMany()
                .HasForeignKey(i => i.OrganisationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
