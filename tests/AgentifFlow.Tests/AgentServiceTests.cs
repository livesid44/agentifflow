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

    // ── Skill tests ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSkillsAsync_ReturnsEmpty_ForNewAgent()
    {
        using var db = CreateDb(nameof(GetSkillsAsync_ReturnsEmpty_ForNewAgent));
        var svc = CreateSvc(db);

        var agent = await svc.CreateAsync(new CreateAgentRequest { Name = "Agent1" });
        var skills = await svc.GetSkillsAsync(agent.Id);

        Assert.Empty(skills);
    }

    [Fact]
    public async Task SetSkillsAsync_AssignsSkillsToAgent()
    {
        using var db = CreateDb(nameof(SetSkillsAsync_AssignsSkillsToAgent));
        var svc = CreateSvc(db);

        var agent = await svc.CreateAsync(new CreateAgentRequest { Name = "Agent2" });

        var request = new SetSkillsRequest
        {
            Skills = new()
            {
                new AgentSkillEntry { SkillType = "FileMonitoring",  IsEnabled = true  },
                new AgentSkillEntry { SkillType = "DataValidation",  IsEnabled = true  },
                new AgentSkillEntry { SkillType = "SqlManagement",   IsEnabled = false },
            }
        };

        var skills = await svc.SetSkillsAsync(agent.Id, request);

        Assert.NotNull(skills);
        Assert.Equal(3, skills!.Count);
        Assert.Contains(skills, s => s.SkillType == "FileMonitoring" && s.IsEnabled);
        Assert.Contains(skills, s => s.SkillType == "SqlManagement"  && !s.IsEnabled);
    }

    [Fact]
    public async Task SetSkillsAsync_ReturnsNull_ForMissingAgent()
    {
        using var db = CreateDb(nameof(SetSkillsAsync_ReturnsNull_ForMissingAgent));
        var svc = CreateSvc(db);

        var result = await svc.SetSkillsAsync(999, new SetSkillsRequest());

        Assert.Null(result);
    }

    [Fact]
    public async Task SetSkillsAsync_ReplacesExistingSkills()
    {
        using var db = CreateDb(nameof(SetSkillsAsync_ReplacesExistingSkills));
        var svc = CreateSvc(db);

        var agent = await svc.CreateAsync(new CreateAgentRequest { Name = "Agent3" });

        // First assignment: 2 skills
        await svc.SetSkillsAsync(agent.Id, new SetSkillsRequest
        {
            Skills = new()
            {
                new AgentSkillEntry { SkillType = "EmailMonitoring", IsEnabled = true },
                new AgentSkillEntry { SkillType = "FileMonitoring",  IsEnabled = true },
            }
        });

        // Replace with single skill
        var updated = await svc.SetSkillsAsync(agent.Id, new SetSkillsRequest
        {
            Skills = new()
            {
                new AgentSkillEntry { SkillType = "SqlManagement", IsEnabled = true },
            }
        });

        Assert.NotNull(updated);
        Assert.Single(updated!);
        Assert.Equal("SqlManagement", updated[0].SkillType);
    }

    [Fact]
    public async Task GetSkillCatalogue_ReturnsFourSkills()
    {
        using var db = CreateDb(nameof(GetSkillCatalogue_ReturnsFourSkills));
        var svc = CreateSvc(db);

        var catalogue = svc.GetSkillCatalogue().ToList();

        Assert.Equal(4, catalogue.Count);
        Assert.Contains(catalogue, s => s.Type == "EmailMonitoring");
        Assert.Contains(catalogue, s => s.Type == "FileMonitoring");
        Assert.Contains(catalogue, s => s.Type == "DataValidation");
        Assert.Contains(catalogue, s => s.Type == "SqlManagement");
    }

    [Fact]
    public async Task AgentDto_IncludesSkills_WhenLoaded()
    {
        using var db = CreateDb(nameof(AgentDto_IncludesSkills_WhenLoaded));
        var svc = CreateSvc(db);

        var agent = await svc.CreateAsync(new CreateAgentRequest { Name = "Agent4" });
        await svc.SetSkillsAsync(agent.Id, new SetSkillsRequest
        {
            Skills = new()
            {
                new AgentSkillEntry { SkillType = "FileMonitoring", IsEnabled = true },
            }
        });

        var loaded = await svc.GetByIdAsync(agent.Id);

        Assert.NotNull(loaded);
        Assert.Single(loaded!.Skills);
        Assert.Equal("FileMonitoring", loaded.Skills[0].SkillType);
    }
}
