using Microsoft.EntityFrameworkCore;

namespace ArkaneSystems.Raven.Core.Infrastructure.Persistence;

// The EF Core DbContext for Raven's SQLite database.
// Registered as a DbContextFactory in Program.cs so each SqliteSessionStore
// operation can open and dispose its own short-lived DbContext instance safely.
public class RavenDbContext (DbContextOptions<RavenDbContext> options) : DbContext (options)
{
  // The Sessions table — one row per active or historical client session.
  public DbSet<SessionRecord> Sessions => Set<SessionRecord> ();

  protected override void OnModelCreating (ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<SessionRecord> (entity =>
    {
      // SessionId is both the primary key and the client-facing handle,
      // so it is stored as a string (Guid) rather than an auto-increment int.
      entity.HasKey (e => e.SessionId);
      entity.Property (e => e.SessionId).IsRequired ();
      entity.Property (e => e.ConversationId).IsRequired ();
      entity.Property (e => e.CreatedAt).IsRequired ();
      entity.Property (e => e.LastActivityAt);
    });
  }
}