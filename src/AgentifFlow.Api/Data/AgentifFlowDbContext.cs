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
    }
}
