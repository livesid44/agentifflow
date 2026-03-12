using AgentifFlow.Api.Data;
using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AgentifFlow.Tests;

public class AppConfigurationServiceTests
{
    private static AgentifFlowDbContext CreateDb(string name)
    {
        var options = new DbContextOptionsBuilder<AgentifFlowDbContext>()
            .UseInMemoryDatabase(name)
            .Options;
        return new AgentifFlowDbContext(options);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReturnsEmptyDto_WhenNoRowExists()
    {
        using var db = CreateDb(nameof(GetConfigurationAsync_ReturnsEmptyDto_WhenNoRowExists));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        var result = await svc.GetConfigurationAsync();

        Assert.NotNull(result);
        Assert.Null(result.GraphTenantId);
        Assert.Null(result.OpenAiEndpoint);
    }

    [Fact]
    public async Task UpdateConfigurationAsync_CreatesRow_WhenNoneExists()
    {
        using var db = CreateDb(nameof(UpdateConfigurationAsync_CreatesRow_WhenNoneExists));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        var request = new UpdateAppConfigurationRequest
        {
            GraphTenantId = "tenant-1",
            GraphClientId = "client-1",
            GraphClientSecret = "super-secret",
            OpenAiEndpoint = "https://ai.openai.azure.com/",
            OpenAiApiKey = "api-key",
            OpenAiDeploymentName = "gpt-4o",
            BlobContainerName = "files",
            BlobStorageConnectionString = "DefaultEndpointsProtocol=https;...",
            SqlConnectionString = "Server=sql;Database=db;"
        };

        var result = await svc.UpdateConfigurationAsync(request, "admin@contoso.com");

        Assert.Equal("tenant-1", result.GraphTenantId);
        Assert.Equal("client-1", result.GraphClientId);
        Assert.Equal("gpt-4o", result.OpenAiDeploymentName);
        Assert.Equal("files", result.BlobContainerName);
        Assert.Equal("admin@contoso.com", result.UpdatedBy);
        Assert.NotNull(result.UpdatedAt);
    }

    [Fact]
    public async Task UpdateConfigurationAsync_MasksSecrets_InReturnedDto()
    {
        using var db = CreateDb(nameof(UpdateConfigurationAsync_MasksSecrets_InReturnedDto));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        var request = new UpdateAppConfigurationRequest
        {
            GraphClientSecret = "my-secret",
            OpenAiApiKey = "my-api-key",
            BlobStorageConnectionString = "connection-string",
            SqlConnectionString = "sql-string"
        };

        var result = await svc.UpdateConfigurationAsync(request, "user");

        // Secrets must be masked, never returned as plaintext
        Assert.Equal("••••••••", result.GraphClientSecret);
        Assert.Equal("••••••••", result.OpenAiApiKey);
        Assert.Equal("••••••••", result.BlobStorageConnectionString);
        Assert.Equal("••••••••", result.SqlConnectionString);
    }

    [Fact]
    public async Task UpdateConfigurationAsync_UpdatesExistingRow()
    {
        using var db = CreateDb(nameof(UpdateConfigurationAsync_UpdatesExistingRow));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        // Create initial row
        await svc.UpdateConfigurationAsync(
            new UpdateAppConfigurationRequest { GraphTenantId = "old-tenant" }, "user1");

        // Update just the tenant id
        var result = await svc.UpdateConfigurationAsync(
            new UpdateAppConfigurationRequest { GraphTenantId = "new-tenant" }, "user2");

        Assert.Equal("new-tenant", result.GraphTenantId);
        Assert.Equal("user2", result.UpdatedBy);

        // Should still be only one row in the DB
        Assert.Equal(1, await db.AppConfigurations.CountAsync());
    }

    [Fact]
    public async Task UpdateConfigurationAsync_DoesNotOverwriteFieldsWithNull()
    {
        using var db = CreateDb(nameof(UpdateConfigurationAsync_DoesNotOverwriteFieldsWithNull));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        await svc.UpdateConfigurationAsync(
            new UpdateAppConfigurationRequest { GraphTenantId = "tenant-x", OpenAiDeploymentName = "gpt-4" }, "u1");

        // Only update deployment name — tenant id should be preserved
        await svc.UpdateConfigurationAsync(
            new UpdateAppConfigurationRequest { OpenAiDeploymentName = "gpt-4o" }, "u2");

        var config = await db.AppConfigurations.FirstAsync();
        Assert.Equal("tenant-x", config.GraphTenantId);
        Assert.Equal("gpt-4o", config.OpenAiDeploymentName);
    }

    [Fact]
    public async Task GetConfigurationAsync_ReturnsLatestValues()
    {
        using var db = CreateDb(nameof(GetConfigurationAsync_ReturnsLatestValues));
        var svc = new AppConfigurationService(db, NullLogger<AppConfigurationService>.Instance);

        await svc.UpdateConfigurationAsync(
            new UpdateAppConfigurationRequest { OpenAiEndpoint = "https://v1.openai.azure.com/" }, "u");

        var result = await svc.GetConfigurationAsync();

        Assert.Equal("https://v1.openai.azure.com/", result.OpenAiEndpoint);
    }
}
