using Microsoft.EntityFrameworkCore;

namespace ContentCms.API.Models
{
    public class ContentCmsDbContext : DbContext
    {
        public ContentCmsDbContext(DbContextOptions<ContentCmsDbContext> options) : base(options)
        {
        }

        public DbSet<UserModel> Users { get; set; } = null!;

        public DbSet<ContentModel> Contents { get; set; } = null!;
        public DbSet<GroupModel> Groups { get; set; } = null!;
        public DbSet<ContentActionLog> ContentActionLogs { get; set; } = null!;

        public DbSet<PluginModel> Plugins { get; set; } = null!;
        public DbSet<PluginAssignmentModel> PluginAssignments { get; set; } = null!;
        public DbSet<PluginStorageEntry> PluginStorageEntries { get; set; } = null!;
        public DbSet<PluginLogEntry> PluginLogEntries { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure User model
            modelBuilder.Entity<UserModel>(entity =>
            {
                entity.HasIndex(u => u.Username).IsUnique();
                entity.HasIndex(u => u.Email).IsUnique();
            });

            // Configure Group model
            modelBuilder.Entity<GroupModel>(entity =>
            {
                entity.HasOne(g => g.Owner)
                      .WithMany()
                      .HasForeignKey(g => g.OwnerId)
                      .OnDelete(DeleteBehavior.Restrict); // Don't cascade delete user
            });

            // Configure Content model
            modelBuilder.Entity<ContentModel>(entity =>
            {
                entity.HasIndex(c => c.Path).IsUnique();
                
                // Configure the relationship between Content and User (Owner)
                entity.HasOne(c => c.Owner)
                      .WithMany(u => u.OwnedContent)
                      .HasForeignKey(c => c.OwnerId)
                      .OnDelete(DeleteBehavior.Cascade);

                // Configure relationship between Content and Group
                entity.HasOne(c => c.Group)
                      .WithMany(g => g.Contents)
                      .HasForeignKey(c => c.GroupId)
                      .OnDelete(DeleteBehavior.SetNull);
            });

            // Configure ContentActionLog model
            modelBuilder.Entity<ContentActionLog>(entity =>
            {
                entity.HasOne(log => log.Content)
                      .WithMany()
                      .HasForeignKey(log => log.ContentId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure plugins
            modelBuilder.Entity<PluginModel>(entity =>
            {
                entity.HasIndex(p => p.Key).IsUnique();
            });

            modelBuilder.Entity<PluginAssignmentModel>(entity =>
            {
                entity.HasIndex(a => new { a.PluginId, a.UserId }).IsUnique();

                entity.HasOne(a => a.Plugin)
                      .WithMany(p => p.Assignments)
                      .HasForeignKey(a => a.PluginId)
                      .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(a => a.User)
                      .WithMany()
                      .HasForeignKey(a => a.UserId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PluginStorageEntry>(entity =>
            {
                entity.HasIndex(s => new { s.PluginId, s.UserId, s.Key }).IsUnique();

                entity.HasOne(s => s.Plugin)
                      .WithMany()
                      .HasForeignKey(s => s.PluginId)
                      .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<PluginLogEntry>(entity =>
            {
                entity.HasIndex(l => new { l.PluginId, l.Timestamp });

                entity.HasOne(l => l.Plugin)
                      .WithMany()
                      .HasForeignKey(l => l.PluginId)
                      .OnDelete(DeleteBehavior.Cascade);
            });
        }
    }
}
