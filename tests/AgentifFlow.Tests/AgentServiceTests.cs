using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentifFlow.Tests;

public class AgentServiceTests
{
    private static AgentifFlowDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AgentifFlowDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AgentifFlowDbContext(options);
    }

    private static AgentService CreateSvc(AgentifFlowDbContext db)
        => new(db, NullLogger<AgentService>.Instance);

    [Fact]
    public async Task CreateAsync_PersistsAgent()
    {
        using var db = CreateDb(nameof(CreateAsync_PersistsAgent));
        var svc = CreateSvc(db);

        var dto = await svc.CreateAsync(new CreateAgentRequest
        {
            Name        = "Test Agent",
            Description = "desc",
            IsEnabled   = true,
        });

        Assert.True(dto.Id > 0);
        Assert.Equal("Test Agent", dto.Name);
        Assert.True(dto.IsEnabled);
    }

    [Fact]
    public async Task CreateAsync_WithFileTargets_PersistsTargets()
    {
        using var db = CreateDb(nameof(CreateAsync_WithFileTargets_PersistsTargets));
        var svc = CreateSvc(db);

        var dto = await svc.CreateAsync(new CreateAgentRequest
        {
            Name = "Agent with targets",
            FileTargets = new()
            {
                new AgentFileTargetRequest { FilePattern = "daily_report", AppendDate = true,  IsRequired = true  },
                new AgentFileTargetRequest { FilePattern = "weekly_stats", AppendDate = false, IsRequired = false },
            }
        });

        Assert.Equal(2, dto.FileTargets.Count);
        Assert.Contains(dto.FileTargets, t => t.FilePattern == "daily_report" && t.IsRequired);
        Assert.Contains(dto.FileTargets, t => t.FilePattern == "weekly_stats" && !t.IsRequired);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsList()
    {
        using var db = CreateDb(nameof(GetAllAsync_ReturnsList));
        var svc = CreateSvc(db);

        await svc.CreateAsync(new CreateAgentRequest { Name = "Agent A" });
        await svc.CreateAsync(new CreateAgentRequest { Name = "Agent B" });

        var list = (await svc.GetAllAsync()).ToList();

        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenNotFound()
    {
        using var db = CreateDb(nameof(GetByIdAsync_ReturnsNull_WhenNotFound));
        var svc = CreateSvc(db);

        var dto = await svc.GetByIdAsync(999);

        Assert.Null(dto);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesFields()
    {
        using var db = CreateDb(nameof(UpdateAsync_UpdatesFields));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync(new CreateAgentRequest { Name = "Old Name", IsEnabled = true });
        var updated = await svc.UpdateAsync(created.Id, new UpdateAgentRequest
        {
            Name      = "New Name",
            IsEnabled = false,
        });

        Assert.NotNull(updated);
        Assert.Equal("New Name", updated!.Name);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task UpdateAsync_ReplacesFileTargets_WhenProvided()
    {
        using var db = CreateDb(nameof(UpdateAsync_ReplacesFileTargets_WhenProvided));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync(new CreateAgentRequest
        {
            Name        = "Agent",
            FileTargets = new() { new AgentFileTargetRequest { FilePattern = "old" } },
        });
        Assert.Single(created.FileTargets);

        var updated = await svc.UpdateAsync(created.Id, new UpdateAgentRequest
        {
            FileTargets = new()
            {
                new AgentFileTargetRequest { FilePattern = "new1" },
                new AgentFileTargetRequest { FilePattern = "new2" },
            }
        });

        Assert.NotNull(updated);
        Assert.Equal(2, updated!.FileTargets.Count);
        Assert.DoesNotContain(updated.FileTargets, t => t.FilePattern == "old");
    }

    [Fact]
    public async Task UpdateAsync_ReturnsNull_WhenNotFound()
    {
        using var db = CreateDb(nameof(UpdateAsync_ReturnsNull_WhenNotFound));
        var svc = CreateSvc(db);

        var result = await svc.UpdateAsync(42, new UpdateAgentRequest { Name = "x" });

        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_RemovesAgent()
    {
        using var db = CreateDb(nameof(DeleteAsync_RemovesAgent));
        var svc = CreateSvc(db);

        var created = await svc.CreateAsync(new CreateAgentRequest { Name = "ToDelete" });
        var deleted  = await svc.DeleteAsync(created.Id);
        var fetched  = await svc.GetByIdAsync(created.Id);

        Assert.True(deleted);
        Assert.Null(fetched);
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalse_WhenNotFound()
    {
        using var db = CreateDb(nameof(DeleteAsync_ReturnsFalse_WhenNotFound));
        var svc = CreateSvc(db);

        var result = await svc.DeleteAsync(999);

        Assert.False(result);
    }

    [Fact]
    public async Task GetEnabledAgentsWithTargetsAsync_ReturnsOnlyEnabled()
    {
        using var db = CreateDb(nameof(GetEnabledAgentsWithTargetsAsync_ReturnsOnlyEnabled));
        var svc = CreateSvc(db);

        await svc.CreateAsync(new CreateAgentRequest { Name = "Enabled", IsEnabled = true });
        await svc.CreateAsync(new CreateAgentRequest { Name = "Disabled", IsEnabled = false });

        var enabled = (await svc.GetEnabledAgentsWithTargetsAsync()).ToList();

        Assert.Single(enabled);
        Assert.Equal("Enabled", enabled[0].Name);
    }
}
