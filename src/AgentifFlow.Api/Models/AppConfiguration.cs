using System.ComponentModel.DataAnnotations;

namespace AgentifFlow.Api.Models;

/// <summary>Persisted application configuration for all Azure service integrations.</summary>
public class AppConfiguration
{
    [Key]
    public int Id { get; set; }

    // ── Graph API / Email ────────────────────────────────────────────────────
    [MaxLength(200)]
    public string? GraphTenantId { get; set; }

    [MaxLength(200)]
    public string? GraphClientId { get; set; }

    [MaxLength(500)]
    public string? GraphClientSecret { get; set; }

    [MaxLength(500)]
    public string? GraphScopes { get; set; }

    // ── Azure OpenAI ─────────────────────────────────────────────────────────
    [MaxLength(500)]
    public string? OpenAiEndpoint { get; set; }

    [MaxLength(500)]
    public string? OpenAiApiKey { get; set; }

    [MaxLength(200)]
    public string? OpenAiDeploymentName { get; set; }

    // ── Azure Blob Storage ───────────────────────────────────────────────────
    [MaxLength(1000)]
    public string? BlobStorageConnectionString { get; set; }

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    // ── SQL Database ─────────────────────────────────────────────────────────
    [MaxLength(1000)]
    public string? SqlConnectionString { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(200)]
    public string? UpdatedBy { get; set; }
}

/// <summary>DTO returned to the client — secrets are masked.</summary>
public class AppConfigurationDto
{
    public string? GraphTenantId { get; set; }
    public string? GraphClientId { get; set; }
    public string? GraphClientSecret { get; set; }
    public string? GraphScopes { get; set; }
    public string? OpenAiEndpoint { get; set; }
    public string? OpenAiApiKey { get; set; }
    public string? OpenAiDeploymentName { get; set; }
    public string? BlobStorageConnectionString { get; set; }
    public string? BlobContainerName { get; set; }
    public string? SqlConnectionString { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

/// <summary>Request body for updating configuration.</summary>
public class UpdateAppConfigurationRequest
{
    [MaxLength(200)]
    public string? GraphTenantId { get; set; }

    [MaxLength(200)]
    public string? GraphClientId { get; set; }

    [MaxLength(500)]
    public string? GraphClientSecret { get; set; }

    [MaxLength(500)]
    public string? GraphScopes { get; set; }

    [MaxLength(500)]
    public string? OpenAiEndpoint { get; set; }

    [MaxLength(500)]
    public string? OpenAiApiKey { get; set; }

    [MaxLength(200)]
    public string? OpenAiDeploymentName { get; set; }

    [MaxLength(1000)]
    public string? BlobStorageConnectionString { get; set; }

    [MaxLength(200)]
    public string? BlobContainerName { get; set; }

    [MaxLength(1000)]
    public string? SqlConnectionString { get; set; }
}
