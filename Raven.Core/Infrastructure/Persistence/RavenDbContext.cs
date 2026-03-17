using Microsoft.EntityFrameworkCore;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

public class RavenDbContext(DbContextOptions<RavenDbContext> options) : DbContext(options)
{
    public DbSet<SessionRecord> Sessions => Set<SessionRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SessionRecord>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.SessionId).IsRequired();
            entity.Property(e => e.ConversationId).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.LastActivityAt);
        });
    }
}
