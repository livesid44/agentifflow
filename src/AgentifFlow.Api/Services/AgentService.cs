using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

public class AgentService : IAgentService
{
    private readonly AgentifFlowDbContext _db;
    private readonly ILogger<AgentService> _logger;

    public AgentService(AgentifFlowDbContext db, ILogger<AgentService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    public async Task<IEnumerable<AgentDto>> GetAllAsync()
    {
        var agents = await _db.Agents
            .Include(a => a.FileTargets)
            .Include(a => a.Skills)
            .OrderBy(a => a.Name)
            .ToListAsync();

        var result = new List<AgentDto>(agents.Count);
        foreach (var a in agents)
            result.Add(await EnrichWithStatsAsync(a));

        return result;
    }

    public async Task<AgentDto?> GetByIdAsync(int id)
    {
        var agent = await _db.Agents
            .Include(a => a.FileTargets)
            .Include(a => a.Skills)
            .FirstOrDefaultAsync(a => a.Id == id);

        return agent is null ? null : await EnrichWithStatsAsync(agent);
    }

    public async Task<AgentDto> CreateAsync(CreateAgentRequest request)
    {
        var agent = new Agent
        {
            Name                     = request.Name,
            Description              = request.Description,
            IsEnabled                = request.IsEnabled,
            BlobContainerName        = request.BlobContainerName,
            NotificationEmail        = request.NotificationEmail,
            NotifyOnSuccess          = request.NotifyOnSuccess,
            NotifyOnFileNotFound     = request.NotifyOnFileNotFound,
            NotifyOnDataIssue        = request.NotifyOnDataIssue,
            MaxRetryCount            = request.MaxRetryCount,
            AutoRetryIntervalMinutes = request.AutoRetryIntervalMinutes,
            PollingIntervalMinutes   = request.PollingIntervalMinutes,
            SqlPushEnabled           = request.SqlPushEnabled,
            SqlTargetTable           = request.SqlTargetTable,
            SqlColumnMappingJson     = request.SqlColumnMappingJson,
            BlobArchiveFilePattern   = request.BlobArchiveFilePattern,
            BlobArchiveAppendDate    = request.BlobArchiveAppendDate,
            CreatedAt                = DateTime.UtcNow,
            UpdatedAt                = DateTime.UtcNow,
        };

        foreach (var t in request.FileTargets)
            agent.FileTargets.Add(new AgentFileTarget
            {
                FilePattern = t.FilePattern,
                AppendDate  = t.AppendDate,
                IsRequired  = t.IsRequired,
            });

        _db.Agents.Add(agent);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Agent '{Name}' created (Id={Id})", agent.Name, agent.Id);
        return await EnrichWithStatsAsync(agent);
    }

    public async Task<AgentDto?> UpdateAsync(int id, UpdateAgentRequest request)
    {
        var agent = await _db.Agents
            .Include(a => a.FileTargets)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (agent is null) return null;

        if (request.Name is not null)                     agent.Name                     = request.Name;
        if (request.Description is not null)              agent.Description              = request.Description;
        if (request.IsEnabled.HasValue)                   agent.IsEnabled                = request.IsEnabled.Value;
        if (request.BlobContainerName is not null)        agent.BlobContainerName        = request.BlobContainerName;
        if (request.NotificationEmail is not null)        agent.NotificationEmail        = request.NotificationEmail;
        if (request.NotifyOnSuccess.HasValue)             agent.NotifyOnSuccess          = request.NotifyOnSuccess.Value;
        if (request.NotifyOnFileNotFound.HasValue)        agent.NotifyOnFileNotFound     = request.NotifyOnFileNotFound.Value;
        if (request.NotifyOnDataIssue.HasValue)           agent.NotifyOnDataIssue        = request.NotifyOnDataIssue.Value;
        if (request.MaxRetryCount.HasValue)               agent.MaxRetryCount            = request.MaxRetryCount.Value;
        if (request.AutoRetryIntervalMinutes.HasValue)    agent.AutoRetryIntervalMinutes = request.AutoRetryIntervalMinutes.Value;
        if (request.PollingIntervalMinutes.HasValue)      agent.PollingIntervalMinutes   = request.PollingIntervalMinutes.Value;
        if (request.SqlPushEnabled.HasValue)              agent.SqlPushEnabled           = request.SqlPushEnabled.Value;
        if (request.SqlTargetTable is not null)           agent.SqlTargetTable           = request.SqlTargetTable;
        if (request.SqlColumnMappingJson is not null)     agent.SqlColumnMappingJson     = request.SqlColumnMappingJson;
        if (request.BlobArchiveFilePattern is not null)   agent.BlobArchiveFilePattern   = request.BlobArchiveFilePattern;
        if (request.BlobArchiveAppendDate.HasValue)       agent.BlobArchiveAppendDate    = request.BlobArchiveAppendDate.Value;

        if (request.FileTargets is not null)
        {
            _db.AgentFileTargets.RemoveRange(agent.FileTargets);
            agent.FileTargets.Clear();
            foreach (var t in request.FileTargets)
                agent.FileTargets.Add(new AgentFileTarget
                {
                    FilePattern = t.FilePattern,
                    AppendDate  = t.AppendDate,
                    IsRequired  = t.IsRequired,
                });
        }

        agent.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        _logger.LogInformation("Agent '{Name}' updated (Id={Id})", agent.Name, agent.Id);
        return await EnrichWithStatsAsync(agent);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var agent = await _db.Agents.FindAsync(id);
        if (agent is null) return false;

        _db.Agents.Remove(agent);
        await _db.SaveChangesAsync();
        _logger.LogInformation("Agent Id={Id} deleted.", id);
        return true;
    }

    public async Task<IEnumerable<Agent>> GetEnabledAgentsWithTargetsAsync()
    {
        return await _db.Agents
            .Include(a => a.FileTargets)
            .Include(a => a.Skills)
            .Where(a => a.IsEnabled)
            .ToListAsync();
    }

    // ── Skill operations ──────────────────────────────────────────────────────

    public async Task<List<AgentSkillDto>> GetSkillsAsync(int agentId)
    {
        return await _db.AgentSkills
            .Where(s => s.AgentId == agentId)
            .Select(s => new AgentSkillDto
            {
                Id         = s.Id,
                AgentId    = s.AgentId,
                SkillType  = s.SkillType,
                IsEnabled  = s.IsEnabled,
                ConfigJson = s.ConfigJson,
            })
            .ToListAsync();
    }

    public async Task<List<AgentSkillDto>?> SetSkillsAsync(int agentId, SetSkillsRequest request)
    {
        var agentExists = await _db.Agents.AnyAsync(a => a.Id == agentId);
        if (!agentExists) return null;

        // Remove existing skills then replace atomically.
        var existing = await _db.AgentSkills.Where(s => s.AgentId == agentId).ToListAsync();
        _db.AgentSkills.RemoveRange(existing);

        foreach (var entry in request.Skills)
        {
            _db.AgentSkills.Add(new AgentSkill
            {
                AgentId    = agentId,
                SkillType  = entry.SkillType,
                IsEnabled  = entry.IsEnabled,
                ConfigJson = entry.ConfigJson,
            });
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Skills updated for Agent Id={Id} ({Count} skills)", agentId, request.Skills.Count);
        return await GetSkillsAsync(agentId);
    }

    public IEnumerable<SkillCatalogueDto> GetSkillCatalogue()
    {
        return SkillCatalogue.All.Select(s => new SkillCatalogueDto
        {
            Type        = s.Type.ToString(),
            DisplayName = s.DisplayName,
            Description = s.Description,
            Icon        = s.Icon,
            Provider    = s.Provider,
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<AgentDto> EnrichWithStatsAsync(Agent agent)
    {
        var dto = ToDto(agent);

        var stats = await _db.BlobWatcherJobs
            .Where(j => j.AgentId == agent.Id)
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        foreach (var s in stats)
        {
            switch (s.Status)
            {
                case BlobWatcherJobStatus.Completed:
                    dto.JobsCompleted += s.Count; break;
                case BlobWatcherJobStatus.Failed:
                case BlobWatcherJobStatus.Rejected:
                    dto.JobsFailed += s.Count; break;
                case BlobWatcherJobStatus.AwaitingApproval:
                case BlobWatcherJobStatus.ValidationFailed:
                case BlobWatcherJobStatus.ReplyReceived:
                    dto.JobsAwaitingApproval += s.Count; break;
                default:
                    dto.JobsInProgress += s.Count; break;
            }
        }

        dto.LastActivity = await _db.BlobWatcherJobs
            .Where(j => j.AgentId == agent.Id)
            .MaxAsync(j => (DateTime?)j.UpdatedAt);

        return dto;
    }

    public async Task<string?> GetSqlScriptAsync(int agentId)
    {
        var agent = await _db.Agents
            .Include(a => a.Skills)
            .FirstOrDefaultAsync(a => a.Id == agentId);

        if (agent is null) return null;

        // Determine the table name
        var rawTable = !string.IsNullOrWhiteSpace(agent.SqlTargetTable)
            ? agent.SqlTargetTable
            : System.Text.RegularExpressions.Regex.Replace(
                agent.Name.ToLowerInvariant(), @"[^a-z0-9_]", "_");

        string QuoteIdentifier(string name) => "[" + name.Replace("]", "]]") + "]";

        // Try to extract target column names from SqlColumnMappingJson
        var columns = new List<string>();
        if (!string.IsNullOrWhiteSpace(agent.SqlColumnMappingJson))
        {
            try
            {
                var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMapEntry>>(
                    agent.SqlColumnMappingJson,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (mappings is not null)
                    columns = mappings
                        .Where(m => !string.IsNullOrWhiteSpace(m.Target))
                        .Select(m => m.Target!)
                        .Distinct()
                        .ToList();
            }
            catch { /* ignore malformed JSON */ }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"-- SQL script generated by AgentifFlow for agent: {agent.Name}");
        sb.AppendLine($"-- Table columns are derived from your configured column mapping.");
        sb.AppendLine($"-- If no mapping is configured, columns are inferred from the CSV headers at runtime.");
        sb.AppendLine();

        if (columns.Count > 0)
        {
            var colDefs = string.Join(",\n    ", columns.Select(c => $"{QuoteIdentifier(c)} NVARCHAR(MAX)"));
            sb.AppendLine($"IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = '{rawTable.Trim('[', ']').Split('.').Last().Trim('[', ']')}')");
            sb.AppendLine("BEGIN");
            sb.AppendLine($"    CREATE TABLE {rawTable} (");
            sb.AppendLine($"    {colDefs}");
            sb.AppendLine("    );");
            sb.AppendLine("END");
        }
        else
        {
            sb.AppendLine($"-- No column mapping is configured. The table will be auto-created when the first CSV file arrives.");
            sb.AppendLine($"-- Pre-create the table manually using the column names from your CSV header row:");
            sb.AppendLine($"--");
            sb.AppendLine($"-- CREATE TABLE {rawTable} (");
            sb.AppendLine($"--     [column1] NVARCHAR(MAX),");
            sb.AppendLine($"--     [column2] NVARCHAR(MAX)");
            sb.AppendLine($"-- );");
        }

        return sb.ToString();
    }

    private sealed class ColumnMapEntry
    {
        public string? Source { get; set; }
        public string? Target { get; set; }
    }

    private static AgentDto ToDto(Agent a) => new()
    {
        Id                       = a.Id,
        Name                     = a.Name,
        Description              = a.Description,
        IsEnabled                = a.IsEnabled,
        BlobContainerName        = a.BlobContainerName,
        NotificationEmail        = a.NotificationEmail,
        NotifyOnSuccess          = a.NotifyOnSuccess,
        NotifyOnFileNotFound     = a.NotifyOnFileNotFound,
        NotifyOnDataIssue        = a.NotifyOnDataIssue,
        MaxRetryCount            = a.MaxRetryCount,
        AutoRetryIntervalMinutes = a.AutoRetryIntervalMinutes,
        PollingIntervalMinutes   = a.PollingIntervalMinutes,
        SqlPushEnabled           = a.SqlPushEnabled,
        SqlTargetTable           = a.SqlTargetTable,
        SqlColumnMappingJson     = a.SqlColumnMappingJson,
        BlobArchiveFilePattern   = a.BlobArchiveFilePattern,
        BlobArchiveAppendDate    = a.BlobArchiveAppendDate,
        CreatedAt                = a.CreatedAt,
        UpdatedAt                = a.UpdatedAt,
        FileTargets              = a.FileTargets.Select(t => new AgentFileTargetDto
        {
            Id          = t.Id,
            AgentId     = t.AgentId,
            FilePattern = t.FilePattern,
            AppendDate  = t.AppendDate,
            IsRequired  = t.IsRequired,
        }).ToList(),
        Skills = a.Skills.Select(s => new AgentSkillDto
        {
            Id         = s.Id,
            AgentId    = s.AgentId,
            SkillType  = s.SkillType,
            IsEnabled  = s.IsEnabled,
            ConfigJson = s.ConfigJson,
        }).ToList(),
    };
}
