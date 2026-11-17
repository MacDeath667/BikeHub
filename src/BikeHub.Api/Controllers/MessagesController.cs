using System.ComponentModel.DataAnnotations;
using BikeHub.Api.Services;
using BikeHub.Application.Community;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize, Route("api/messages")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MessagesController(MessageService messages, MessageModerationService moderation) : ControllerBase
{
    [HttpGet("unread-count")]
    public ActionResult<int> UnreadCount() => Ok(messages.UnreadCount());
    [HttpGet("peers/{id:guid}")]
    public ActionResult<MessageTargetDto> Peer(Guid id) => Ok(messages.Target(id));
    [HttpGet("conversations")]
    public ActionResult<Page<ConversationDto>> List([FromQuery] PaginationQuery paging, [FromQuery] bool requests = false, [FromQuery] bool archived = false) =>
        Ok(messages.List(requests, archived, paging.Page, paging.PageSize));
    [HttpGet("conversations/{id:guid}")]
    public ActionResult<ConversationDto> Get(Guid id) => Ok(messages.Get(id));
    [HttpPost("conversations")]
    public ActionResult<ConversationDto> Start(StartConversationRequest request) => Ok(messages.Start(request));
    [HttpPut("conversations/{id:guid}/request")]
    public ActionResult<ConversationDto> Decide(Guid id, MessageRequestDecision request) => Ok(messages.Decide(id, request.Accept));
    [HttpDelete("conversations/{id:guid}/request")]
    public ActionResult<ConversationDto> Withdraw(Guid id) => Ok(messages.Withdraw(id));
    [HttpPut("conversations/{id:guid}/read")]
    public IActionResult Read(Guid id, ReadConversationRequest request) { messages.Read(id, request.Sequence); return NoContent(); }
    [HttpPut("conversations/{id:guid}/archive")]
    public IActionResult Archive(Guid id, ArchiveConversationRequest request) { messages.Archive(id, request.Archived); return NoContent(); }
    [HttpGet("conversations/{id:guid}/messages")]
    public ActionResult<MessagePageDto> History(Guid id, [FromQuery, Range(1, long.MaxValue)] long? beforeSequence,
        [FromQuery, Range(0, long.MaxValue)] long? afterSequence, [FromQuery, Range(1, 50)] int pageSize = 50) => Ok(messages.Messages(id, beforeSequence, afterSequence, pageSize));
    [HttpPost("conversations/{id:guid}/messages")]
    public ActionResult<PrivateMessageDto> Send(Guid id, SendPrivateMessageRequest request) => Ok(messages.Send(id, request));
    [HttpDelete("conversations/{id:guid}/messages/{messageId:guid}")]
    public IActionResult Delete(Guid id, Guid messageId) { messages.DeleteMessage(id, messageId); return NoContent(); }
    [HttpPost("conversations/{id:guid}/messages/{messageId:guid}/reports")]
    public ActionResult<object> Report(Guid id, Guid messageId, PrivateMessageReportRequest request) => Ok(new { id = moderation.Report(id, messageId, request) });
    [HttpGet("blocks")]
    public ActionResult<Page<UserDto>> Blocks([FromQuery] PaginationQuery paging) => Ok(messages.BlockedUsers(paging.Page, paging.PageSize));
    [HttpPut("blocks/{id:guid}")]
    public IActionResult Block(Guid id) { messages.Block(id, true); return NoContent(); }
    [HttpDelete("blocks/{id:guid}")]
    public IActionResult Unblock(Guid id) { messages.Block(id, false); return NoContent(); }
}

[ApiController, Authorize, Route("api/moderation/messages")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class MessageModerationController(MessageModerationService messages) : ControllerBase
{
    [HttpGet("reports")]
    public ActionResult<Page<PrivateMessageReportDto>> Reports([FromQuery] PaginationQuery paging) => Ok(messages.Reports(paging.Page, paging.PageSize));
    [HttpPost("reports/{id:guid}/resolve")]
    public IActionResult Resolve(Guid id, PrivateMessageResolutionRequest request) { messages.Resolve(id, request.Resolution); return NoContent(); }
    [HttpPut("{id:guid}/visibility")]
    public IActionResult Visibility(Guid id, VisibilityRequest request) { messages.Visibility(id, request.Hidden); return NoContent(); }
}
