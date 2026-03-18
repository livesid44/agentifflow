namespace AgentifFlow.Api.Services;

/// <summary>Validates a raw CSV string for structure and data quality.</summary>
public interface ICsvValidationService
{
    CsvValidationResult Validate(string csvContent, string[]? requiredColumns = null);
}

public class CsvValidationResult
{
    public bool IsValid { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<string> Errors { get; init; } = [];
}
