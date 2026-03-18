using AgentifFlow.Api.Services;

namespace AgentifFlow.Tests;

public class CsvValidationServiceTests
{
    private readonly CsvValidationService _svc = new();

    [Fact]
    public void Validate_ValidCsv_ReturnsIsValidTrue()
    {
        const string csv = "Name,Age,City\nAlice,30,London\nBob,25,Paris";
        var result = _svc.Validate(csv);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(3, result.ColumnCount);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_EmptyContent_ReturnsError()
    {
        var result = _svc.Validate("");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    [Fact]
    public void Validate_HeaderOnlyNoCsv_ReturnsError()
    {
        var result = _svc.Validate("Name,Age");
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("header row") || e.Contains("data row"));
    }

    [Fact]
    public void Validate_MismatchedColumns_ReturnsError()
    {
        const string csv = "Name,Age,City\nAlice,30\nBob,25,Paris";
        var result = _svc.Validate(csv);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Row 2"));
    }

    [Fact]
    public void Validate_RequiredColumnMissing_ReturnsError()
    {
        const string csv = "Name,Age\nAlice,30";
        var result = _svc.Validate(csv, requiredColumns: ["Name", "Email"]);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Email"));
    }

    [Fact]
    public void Validate_RequiredColumnsPresent_ReturnsValid()
    {
        const string csv = "Name,Age,Email\nAlice,30,alice@example.com";
        var result = _svc.Validate(csv, requiredColumns: ["Name", "Email"]);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_QuotedFields_HandledCorrectly()
    {
        const string csv = "Name,Description\nAlice,\"Has, a comma\"\nBob,Normal";
        var result = _svc.Validate(csv);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.RowCount);
    }

    [Fact]
    public void Validate_WindowsLineEndings_Handled()
    {
        const string csv = "Name,Age\r\nAlice,30\r\nBob,25";
        var result = _svc.Validate(csv);
        Assert.True(result.IsValid);
        Assert.Equal(2, result.RowCount);
    }
}
