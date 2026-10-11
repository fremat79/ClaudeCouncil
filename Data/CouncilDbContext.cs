using ClaudeCouncilManager.Models;
using ClaudeCouncilManager.Services;
using Microsoft.EntityFrameworkCore;

namespace ClaudeCouncilManager.Data;

/// <summary>
/// SQLite store for experts, councils and their members
/// (<c>%LOCALAPPDATA%\ClaudeCouncilManager\claudecouncil.db</c>). The app keeps one context for its whole
/// lifetime: view models edit the tracked entities and <c>SaveChanges</c> runs debounced.
/// Schema changes go through migrations (<c>dotnet ef migrations add &lt;Name&gt;</c>).
/// </summary>
public sealed class CouncilDbContext : DbContext
{
    public CouncilDbContext()
    {
    }

    /// <summary>For tests or tools that point the context at another database.</summary>
    public CouncilDbContext(DbContextOptions<CouncilDbContext> options) : base(options)
    {
    }

    public DbSet<Expert> Experts => Set<Expert>();

    public DbSet<Council> Councils => Set<Council>();

    public DbSet<CouncilMember> CouncilMembers => Set<CouncilMember>();

    public DbSet<CouncilSession> CouncilSessions => Set<CouncilSession>();

    public DbSet<AppSettings> Settings => Set<AppSettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        if (!options.IsConfigured)
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}");
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Expert>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(Naming.MaxLength);
            e.Property(x => x.Folder).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.Property(x => x.Color).HasMaxLength(16);
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.Effort).HasMaxLength(16);
        });

        model.Entity<Council>(c =>
        {
            c.HasKey(x => x.Id);
            c.Property(x => x.Name).IsRequired().HasMaxLength(Naming.MaxLength);
            c.Property(x => x.WorkingFolder).IsRequired();
            c.Property(x => x.InitialPrompt).IsRequired();
            c.Property(x => x.ExtraInstructions).IsRequired();
            c.Ignore(x => x.MemberIds);
            c.HasMany(x => x.Members).WithOne().HasForeignKey(m => m.CouncilId).OnDelete(DeleteBehavior.Cascade);
            c.HasMany(x => x.Sessions).WithOne().HasForeignKey(s => s.CouncilId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<CouncilMember>(m =>
        {
            m.HasKey(x => new { x.CouncilId, x.ExpertId });
            m.HasOne<Expert>().WithMany().HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });

        model.Entity<AppSettings>(s =>
        {
            s.HasKey(x => x.Id);
            s.Property(x => x.Id).ValueGeneratedNever();
            s.Property(x => x.CustomProgram).IsRequired();
            s.Property(x => x.CustomArguments).IsRequired();
        });

        model.Entity<CouncilSession>(s =>
        {
            s.HasKey(x => new { x.CouncilId, x.ExpertId });
            s.HasOne<Expert>().WithMany().HasForeignKey(x => x.ExpertId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
