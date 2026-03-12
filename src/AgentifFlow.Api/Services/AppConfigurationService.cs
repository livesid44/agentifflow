using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgentifFlow.Api.Services;

public class AppConfigurationService : IAppConfigurationService
{
    private readonly AgentifFlowDbContext _db;
    private readonly ILogger<AppConfigurationService> _logger;

    public AppConfigurationService(AgentifFlowDbContext db, ILogger<AppConfigurationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<AppConfigurationDto> GetConfigurationAsync()
    {
        var config = await _db.AppConfigurations.FirstOrDefaultAsync();
        if (config is null)
            return new AppConfigurationDto();

        return ToDto(config);
    }

    public async Task<AppConfigurationDto> UpdateConfigurationAsync(
        UpdateAppConfigurationRequest request, string updatedBy)
    {
        _logger.LogInformation("Updating app configuration by {User}", updatedBy);

        var config = await _db.AppConfigurations.FirstOrDefaultAsync()
                     ?? new AppConfiguration();

        // Only overwrite a field when the caller actually supplies a non-null value.
        // Supplying an empty string intentionally clears the field.
        if (request.GraphTenantId is not null) config.GraphTenantId = request.GraphTenantId;
        if (request.GraphClientId is not null) config.GraphClientId = request.GraphClientId;
        if (request.GraphClientSecret is not null) config.GraphClientSecret = request.GraphClientSecret;
        if (request.GraphScopes is not null) config.GraphScopes = request.GraphScopes;
        if (request.OpenAiEndpoint is not null) config.OpenAiEndpoint = request.OpenAiEndpoint;
        if (request.OpenAiApiKey is not null) config.OpenAiApiKey = request.OpenAiApiKey;
        if (request.OpenAiDeploymentName is not null) config.OpenAiDeploymentName = request.OpenAiDeploymentName;
        if (request.BlobStorageConnectionString is not null) config.BlobStorageConnectionString = request.BlobStorageConnectionString;
        if (request.BlobContainerName is not null) config.BlobContainerName = request.BlobContainerName;
        if (request.SqlConnectionString is not null) config.SqlConnectionString = request.SqlConnectionString;

        config.UpdatedAt = DateTime.UtcNow;
        config.UpdatedBy = updatedBy;

        if (config.Id == 0)
            _db.AppConfigurations.Add(config);

        await _db.SaveChangesAsync();
        return ToDto(config);
    }

    public async Task<(string? Endpoint, string? ApiKey, string? DeploymentName)> GetOpenAiRawSettingsAsync()
    {
        var config = await _db.AppConfigurations.FirstOrDefaultAsync();
        return (config?.OpenAiEndpoint, config?.OpenAiApiKey, config?.OpenAiDeploymentName);
    }

    private static AppConfigurationDto ToDto(AppConfiguration config) => new()
    {
        GraphTenantId = config.GraphTenantId,
        GraphClientId = config.GraphClientId,
        GraphClientSecret = Mask(config.GraphClientSecret),
        GraphScopes = config.GraphScopes,
        OpenAiEndpoint = config.OpenAiEndpoint,
        OpenAiApiKey = Mask(config.OpenAiApiKey),
        OpenAiDeploymentName = config.OpenAiDeploymentName,
        BlobStorageConnectionString = Mask(config.BlobStorageConnectionString),
        BlobContainerName = config.BlobContainerName,
        SqlConnectionString = Mask(config.SqlConnectionString),
        UpdatedAt = config.UpdatedAt,
        UpdatedBy = config.UpdatedBy
    };

    /// <summary>Returns "••••••••" when a secret is present, or null when empty.</summary>
    private static string? Mask(string? value) =>
        string.IsNullOrWhiteSpace(value) ? value : "••••••••";
}
