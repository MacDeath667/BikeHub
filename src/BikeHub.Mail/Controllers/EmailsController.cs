using System.ComponentModel.DataAnnotations;
using BikeHub.Mail.Contracts;
using BikeHub.Mail.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Mail.Controllers;

[ApiController]
[Route("api/emails")]
[RequestSizeLimit(256_000)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EmailsController(MailQueue queue) : ControllerBase
{
    private const string ConflictMessage = "The message ID is already used for different content.";
    private const string InvalidKeyMessage = "Idempotency-Key must match messageId.";
    private const string InvalidRequestMessage = "Invalid email request.";

    [HttpPost]
    public async Task<IActionResult> Send(SendEmailRequest request, CancellationToken cancellationToken)
    {
        if (Request.Headers.TryGetValue(Constants.IdempotencyKeyHeader, out var key) &&
            (!Guid.TryParse(key.ToString(), out var parsed) || parsed != request.MessageId))
        {
            return BadRequest(new { error = InvalidKeyMessage });
        }
        try
        {
            var result = await queue.EnqueueAsync(request, cancellationToken);
            if (result.Created || result.Email.Status is DeliveryStatus.Queued or DeliveryStatus.Sending)
            {
                return AcceptedAtAction(nameof(Get), new { id = result.Email.MessageId }, result.Email);
            }
            return Ok(result.Email);
        }
        catch (IdempotencyConflictException)
        {
            return Conflict(new { error = ConflictMessage });
        }
        catch (ValidationException)
        {
            return BadRequest(new { error = InvalidRequestMessage });
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await queue.FindAsync(id, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }
}
