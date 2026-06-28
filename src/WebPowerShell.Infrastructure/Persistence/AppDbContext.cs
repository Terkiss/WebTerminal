using Microsoft.EntityFrameworkCore;
using WebPowerShell.Domain.Entities;

namespace WebPowerShell.Infrastructure.Persistence
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User Entity Configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.Username).IsRequired().HasMaxLength(100);
                entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(500);
                entity.Property(e => e.LastPasswordChangeDate).IsRequired();
                entity.Property(e => e.IsActive).IsRequired();
                entity.Property(e => e.CreatedAt).IsRequired();
                entity.Property(e => e.UpdatedAt).IsRequired();
            });

            // AuditLog Entity Configuration
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.HasIndex(e => new { e.UserId, e.ExecutedAt });
                entity.HasIndex(e => e.ExecutedAt);
                entity.HasIndex(e => e.SessionId);
                entity.HasIndex(e => e.ResultStatus);
                entity.HasIndex(e => e.IpAddress);

                entity.Property(e => e.UsernameSnapshot).IsRequired().HasMaxLength(100);
                entity.Property(e => e.SessionId).IsRequired().HasMaxLength(100);
                entity.Property(e => e.TabId).IsRequired().HasMaxLength(50);
                entity.Property(e => e.Command).IsRequired().HasMaxLength(16384); // 16KB Limit
                entity.Property(e => e.IpAddress).IsRequired().HasMaxLength(45); // IPv4 / IPv6
                entity.Property(e => e.ResultStatus).IsRequired().HasMaxLength(50);
                entity.Property(e => e.CorrelationId).IsRequired().HasMaxLength(100);
            });
        }
    }
}
