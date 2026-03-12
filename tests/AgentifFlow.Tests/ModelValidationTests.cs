using AgentifFlow.Api.Models;

namespace AgentifFlow.Tests;

public class ModelValidationTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("Valid Title", true)]
    [InlineData("A very long title that exceeds the maximum allowed length of two hundred characters and should therefore fail validation when checked against the data annotation attribute that enforces the limit of exactly 200 characters.", false)]
    public void AgentTask_Title_Validation(string title, bool isValid)
    {
        var task = new AgentTask { Title = title };
        var results = ValidateModel(task);
        Assert.Equal(isValid, results.Count == 0);
    }

    [Fact]
    public void SendEmailRequest_InvalidEmail_FailsValidation()
    {
        var request = new SendEmailRequest
        {
            To = "not-an-email",
            Subject = "Test",
            Body = "Body"
        };

        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SendEmailRequest_ValidData_PassesValidation()
    {
        var request = new SendEmailRequest
        {
            To = "user@example.com",
            Subject = "Test Subject",
            Body = "This is the email body."
        };

        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void ChatRequest_EmptyMessage_FailsValidation()
    {
        var request = new ChatRequest { Message = "" };
        var results = ValidateModel(request);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void ChatRequest_ValidMessage_PassesValidation()
    {
        var request = new ChatRequest { Message = "Hello, World!" };
        var results = ValidateModel(request);
        Assert.Empty(results);
    }

    [Fact]
    public void ChatRequest_DefaultValues_AreCorrect()
    {
        var request = new ChatRequest { Message = "test" };
        Assert.Equal(1024, request.MaxTokens);
        Assert.Equal(0.7f, request.Temperature);
        Assert.Null(request.History);
        Assert.Null(request.SystemPrompt);
    }

    [Fact]
    public void AgentTask_DefaultStatus_IsPending()
    {
        var task = new AgentTask { Title = "Test" };
        Assert.Equal(AgentTaskStatus.Pending, task.Status);
    }

    private static IList<System.ComponentModel.DataAnnotations.ValidationResult> ValidateModel(object model)
    {
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(model);
        System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, context, results, validateAllProperties: true);
        return results;
    }
}
