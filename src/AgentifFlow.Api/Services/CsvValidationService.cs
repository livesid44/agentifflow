namespace AgentifFlow.Api.Services;

/// <summary>
/// Validates a CSV string:
///   1. Must have at least one header row and one data row.
///   2. Every row must have the same number of columns as the header.
///   3. Required columns (if specified) must be present in the header.
/// </summary>
public class CsvValidationService : ICsvValidationService
{
    public CsvValidationResult Validate(string csvContent, string[]? requiredColumns = null)
    {
        var result = new CsvValidationResult();

        if (string.IsNullOrWhiteSpace(csvContent))
        {
            result.Errors.Add("CSV content is empty.");
            return result;
        }

        var lines = csvContent
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        if (lines.Length < 2)
        {
            result.Errors.Add("CSV must contain a header row and at least one data row.");
            return result;
        }

        var headers = SplitCsvLine(lines[0]);
        result.ColumnCount = headers.Length;

        // Check required columns
        if (requiredColumns is { Length: > 0 })
        {
            foreach (var req in requiredColumns)
            {
                if (!headers.Any(h => h.Equals(req, StringComparison.OrdinalIgnoreCase)))
                    result.Errors.Add($"Required column '{req}' is missing from the header.");
            }
        }

        // Validate each data row
        int dataRows = 0;
        for (int i = 1; i < lines.Length; i++)
        {
            var cols = SplitCsvLine(lines[i]);
            if (cols.Length != headers.Length)
                result.Errors.Add($"Row {i + 1} has {cols.Length} column(s), expected {headers.Length}.");
            else
                dataRows++;
        }

        result.RowCount = dataRows;
        result.IsValid = result.Errors.Count == 0;
        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        // Basic CSV split — handles quoted fields
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++; // skip escaped quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString().Trim());
        return [.. fields];
    }
}
