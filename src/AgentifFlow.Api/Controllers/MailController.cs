using AgentifFlow.Api.Models;
using AgentifFlow.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Identity.Web.Resource;

namespace AgentifFlow.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequiredScope(RequiredScopesConfigurationKey = "AzureAd:Scopes")]
public class MailController : ControllerBase
{
    private readonly IGraphMailService _mailService;
    private readonly ILogger<MailController> _logger;

    public MailController(IGraphMailService mailService, ILogger<MailController> logger)
    {
        _mailService = mailService;
        _logger = logger;
    }

    /// <summary>Gets messages from the authenticated user's inbox.</summary>
    [HttpGet("inbox")]
    [ProducesResponseType(typeof(IEnumerable<EmailMessage>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInbox([FromQuery] int top = 10)
    {
        if (top is < 1 or > 50)
            return BadRequest("Parameter 'top' must be between 1 and 50.");

        var messages = await _mailService.GetInboxMessagesAsync(top);
        return Ok(messages);
    }

    /// <summary>Gets a specific email message by ID.</summary>
    [HttpGet("{messageId}")]
    [ProducesResponseType(typeof(EmailMessage), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMessage(string messageId)
    {
        var message = await _mailService.GetMessageByIdAsync(messageId);
        if (message is null) return NotFound();
        return Ok(message);
    }

    /// <summary>Sends an email on behalf of the authenticated user.</summary>
    [HttpPost("send")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SendEmail([FromBody] SendEmailRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        await _mailService.SendEmailAsync(request);
        return NoContent();
    }

    /// <summary>Deletes an email message by ID.</summary>
    [HttpDelete("{messageId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteMessage(string messageId)
    {
        await _mailService.DeleteMessageAsync(messageId);
        return NoContent();
    }
}
