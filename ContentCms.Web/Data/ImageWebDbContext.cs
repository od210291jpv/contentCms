using Microsoft.EntityFrameworkCore;
using ContentCms.Api.Models;

namespace ContentCms.Web.Data;

public class ImageWebDbContext : DbContext
{
    public ImageWebDbContext(DbContextOptions<ImageWebDbContext> options) : base(options) { }

    public DbSet<Image> Images => Set<Image>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Image>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.HasIndex(e => e.IsDeleted);
        });
    }
}
