namespace AgentifFlow.Api.Models;

// ── ThirdPartyApiIntegration skill config ─────────────────────────────────────

/// <summary>
/// Configuration stored as JSON in <see cref="AgentSkill.ConfigJson"/> for the
/// <see cref="SkillType.ThirdPartyApiIntegration"/> skill.
/// </summary>
public class ThirdPartyApiConfig
{
    /// <summary>Full URL of the external API endpoint to call on each poll cycle.</summary>
    public string? EndpointUrl { get; set; }

    /// <summary>HTTP method: <c>GET</c> (default) or <c>POST</c>.</summary>
    public string RequestMethod { get; set; } = "GET";

    /// <summary>
    /// Authentication type: <c>None</c>, <c>Bearer</c>, <c>ApiKey</c>, or <c>Basic</c>.
    /// </summary>
    public string AuthType { get; set; } = "None";

    /// <summary>
    /// Token or key value used for authentication.
    /// For Bearer: used as the Bearer token.
    /// For ApiKey: the key value sent in <see cref="AuthHeaderName"/>.
    /// For Basic: base-64 encoded "user:password".
    /// </summary>
    public string? AuthToken { get; set; }

    /// <summary>HTTP header name for ApiKey auth (e.g. <c>X-API-Key</c>).</summary>
    public string? AuthHeaderName { get; set; }

    /// <summary>JSON body template for POST requests.  Supports simple placeholders.</summary>
    public string? RequestPayloadTemplate { get; set; }

    /// <summary>
    /// Optional substring that must appear in the response body for the call to be
    /// considered a <em>success</em>.  When absent the call is treated as a failure.
    /// </summary>
    public string? SuccessIndicator { get; set; }

    /// <summary>
    /// Optional substring that, when present in the response body, indicates a
    /// <em>failure</em> regardless of HTTP status code.
    /// </summary>
    public string? FailureIndicator { get; set; }
}

// ── LogAnalysis skill config ───────────────────────────────────────────────────

/// <summary>
/// Configuration stored as JSON in <see cref="AgentSkill.ConfigJson"/> for the
/// <see cref="SkillType.LogAnalysis"/> skill.
/// </summary>
public class LogAnalysisConfig
{
    /// <summary>Email address of the data owner / point-of-contact who receives the correction request.</summary>
    public string? PocEmail { get; set; }

    /// <summary>Human-readable name for the POC (used in the correction email salutation).</summary>
    public string? PocName { get; set; }

    /// <summary>
    /// Optional custom LLM prompt prefix injected before the log content.
    /// When empty, a default prompt focussed on file-naming mismatches is used.
    /// </summary>
    public string? AnalysisPrompt { get; set; }
}
