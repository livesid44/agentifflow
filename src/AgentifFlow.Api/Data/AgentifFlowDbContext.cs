using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Data;

public class AgentifFlowDbContext : DbContext
{
    public AgentifFlowDbContext(DbContextOptions<AgentifFlowDbContext> options)
        : base(options)
    {
    }

    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AppConfiguration> AppConfigurations => Set<AppConfiguration>();
    public DbSet<BlobWatcherJob> BlobWatcherJobs => Set<BlobWatcherJob>();
    public DbSet<Agent> Agents => Set<Agent>();
    public DbSet<AgentFileTarget> AgentFileTargets => Set<AgentFileTarget>();
    public DbSet<AgentSkill> AgentSkills => Set<AgentSkill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AgentTask>(entity =>
        {
            entity.ToTable("AgentTasks");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(2000);
            entity.Property(e => e.AssignedTo).HasMaxLength(200);
            entity.Property(e => e.Result).HasMaxLength(5000);
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);
        });

        modelBuilder.Entity<AppConfiguration>(entity =>
        {
            entity.ToTable("AppConfigurations");
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<BlobWatcherJob>(entity =>
        {
            entity.ToTable("BlobWatcherJobs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.BlobName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.ContainerName).HasMaxLength(200);
            entity.Property(e => e.Status)
                  .HasConversion<string>()
                  .HasMaxLength(50);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);

            entity.HasOne(e => e.Agent)
                  .WithMany(a => a.Jobs)
                  .HasForeignKey(e => e.AgentId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Agent>(entity =>
        {
            entity.ToTable("Agents");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        modelBuilder.Entity<AgentFileTarget>(entity =>
        {
            entity.ToTable("AgentFileTargets");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FilePattern).IsRequired().HasMaxLength(300);

            entity.HasOne(e => e.Agent)
                  .WithMany(a => a.FileTargets)
                  .HasForeignKey(e => e.AgentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AgentSkill>(entity =>
        {
            entity.ToTable("AgentSkills");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.SkillType).IsRequired().HasMaxLength(50);

            entity.HasOne(e => e.Agent)
                  .WithMany(a => a.Skills)
                  .HasForeignKey(e => e.AgentId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
