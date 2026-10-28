using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Permit> Permits => Set<Permit>();
    public DbSet<Inspection> Inspections => Set<Inspection>();
    public DbSet<PermitDocument> Documents => Set<PermitDocument>();
    public DbSet<PermitEvent> Events => Set<PermitEvent>();
    public DbSet<UserSettings> Settings => Set<UserSettings>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Permit>(e =>
        {
            e.HasIndex(p => p.Number).IsUnique();
            e.HasIndex(p => p.Status);
            e.Property(p => p.Valuation).HasPrecision(12, 2);
            e.Property(p => p.FeeTotal).HasPrecision(10, 2);
            e.HasOne(p => p.Applicant).WithMany().HasForeignKey(p => p.ApplicantId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(p => p.Reviewer).WithMany().HasForeignKey(p => p.ReviewerId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Inspection>(e =>
        {
            e.HasIndex(i => new { i.InspectorId, i.ScheduledFor });
            e.HasOne(i => i.Inspector).WithMany().HasForeignKey(i => i.InspectorId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<PermitDocument>()
            .HasOne(d => d.UploadedBy).WithMany().HasForeignKey(d => d.UploadedById).OnDelete(DeleteBehavior.Restrict);

        b.Entity<PermitEvent>()
            .HasOne(ev => ev.User).WithMany().HasForeignKey(ev => ev.UserId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<UserSettings>()
            .HasOne(s => s.User).WithOne().HasForeignKey<UserSettings>(s => s.UserId).OnDelete(DeleteBehavior.Cascade);

        // Store enums as readable text so the database is easy to inspect and reorder-safe.
        foreach (var entity in b.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties().Where(p => p.ClrType.IsEnum))
                prop.SetProviderClrType(typeof(string));
    }
}
