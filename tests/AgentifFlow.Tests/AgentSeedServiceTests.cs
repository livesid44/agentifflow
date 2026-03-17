using AgentifFlow.Api.Data;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentifFlow.Tests;

public class AgentSeedServiceTests
{
    private static AgentifFlowDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AgentifFlowDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AgentifFlowDbContext(options);
    }

    // ── Happy-path: seeds the demo agent on a fresh database ─────────────────

    [Fact]
    public async Task SeedNerandomilastAgent_CreatesAgentWithFourTargets()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_CreatesAgentWithFourTargets));

        await AgentSeedService.SeedNerandomilastAgentAsync(
            db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .Include(a => a.Skills)
            .SingleOrDefaultAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        Assert.NotNull(agent);
        Assert.True(agent.IsEnabled);
        Assert.True(agent.NotifyOnFileNotFound);
        Assert.Equal(4, agent.FileTargets.Count);
        Assert.Equal(2, agent.Skills.Count);
    }

    [Fact]
    public async Task SeedNerandomilastAgent_ControlFileIsRequired_OthersAreOptional()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_ControlFileIsRequired_OthersAreOptional));

        await AgentSeedService.SeedNerandomilastAgentAsync(
            db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .SingleAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        // Exactly one target (control) is required — it triggers the missing-file alert.
        var required = agent.FileTargets.Where(t => t.IsRequired).ToList();
        Assert.Single(required);
        Assert.Contains("control", required[0].FilePattern, StringComparison.OrdinalIgnoreCase);

        // The other three are optional.
        var optional = agent.FileTargets.Where(t => !t.IsRequired).ToList();
        Assert.Equal(3, optional.Count);
    }

    [Fact]
    public async Task SeedNerandomilastAgent_AllTargetsHaveAppendDateFalse()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_AllTargetsHaveAppendDateFalse));

        await AgentSeedService.SeedNerandomilastAgentAsync(
            db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .SingleAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        Assert.All(agent.FileTargets, t => Assert.False(t.AppendDate));
    }

    [Fact]
    public async Task SeedNerandomilastAgent_AllTargetsHaveTxtExtension()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_AllTargetsHaveTxtExtension));

        await AgentSeedService.SeedNerandomilastAgentAsync(
            db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .SingleAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        Assert.All(agent.FileTargets,
            t => Assert.EndsWith(".txt", t.FilePattern, StringComparison.OrdinalIgnoreCase));
    }

    // ── Idempotency: second call must not create duplicates ──────────────────

    [Fact]
    public async Task SeedNerandomilastAgent_CalledTwice_CreatesOnlyOneAgent()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_CalledTwice_CreatesOnlyOneAgent));

        await AgentSeedService.SeedNerandomilastAgentAsync(db, NullLogger.Instance);
        await AgentSeedService.SeedNerandomilastAgentAsync(db, NullLogger.Instance);

        var count = await db.Agents
            .CountAsync(a => a.Name == "Nerandomilast Target Files Monitor");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SeedNerandomilastAgent_CalledTwice_FileTargetCountUnchanged()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_CalledTwice_FileTargetCountUnchanged));

        await AgentSeedService.SeedNerandomilastAgentAsync(db, NullLogger.Instance);
        await AgentSeedService.SeedNerandomilastAgentAsync(db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.FileTargets)
            .SingleAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        Assert.Equal(4, agent.FileTargets.Count);
    }

    // ── Skills ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeedNerandomilastAgent_HasEmailMonitoringAndFileMonitoringSkills()
    {
        using var db = CreateDb(nameof(SeedNerandomilastAgent_HasEmailMonitoringAndFileMonitoringSkills));

        await AgentSeedService.SeedNerandomilastAgentAsync(db, NullLogger.Instance);

        var agent = await db.Agents
            .Include(a => a.Skills)
            .SingleAsync(a => a.Name == "Nerandomilast Target Files Monitor");

        var skillTypes = agent.Skills.Select(s => s.SkillType).ToHashSet();
        Assert.Contains("EmailMonitoring", skillTypes);
        Assert.Contains("FileMonitoring",  skillTypes);
    }
}
