using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using BikeHub.Dal.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Controllers;

[ApiController, Authorize, Route("api/users/me/deletion")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AccountDeletionController(EmailAuthenticationService authentication, AccountDeletionService deletion,
    IAuthenticationStore store, DevelopmentMessages messages, OtpSecurity security, IOptions<AuthOptions> options,
    IHostEnvironment environment) : ControllerBase
{
    private AuthSession Session => HttpContext.Items[typeof(AuthSession)] as AuthSession ?? throw new CommunityProblem(401, "Войдите в аккаунт.");
    private string Binding => Session.Id.ToString("N");
    private bool Available => options.Value.Enabled && options.Value.Store == Constants.AuthenticationStores.Postgres;
    [HttpPost, EnableRateLimiting(Constants.RateLimitPolicies.RequestCode)]
    public async Task<IActionResult> RequestDeletion(CancellationToken ct)
    {
        if (!Available)
        {
            return StatusCode(503);
        }
        try
        {
            var result = await authentication.RequestAsync(Session.Account.Email, Session.Account.DisplayName, Binding, ct, AuthenticationPurpose.AccountDeletion);
            return result is null ? StatusCode(429) : Accepted(result);
        }
        catch (EmailDeliveryException) { return Problem(statusCode: 503, title: "Не удалось отправить код удаления."); }
    }
    private async Task Check(Guid id, CancellationToken ct)
    {
        var challenge = await store.FindChallengeAsync(id, ct);
        if (!Available || challenge is null || challenge.Purpose != AuthenticationPurpose.AccountDeletion || challenge.Email != Session.Account.Email ||
            !OtpSecurity.EqualsHash(challenge.BindingHash, security.Hash(Constants.BrowserBindingPurpose, Binding)))
        {
            throw new CommunityProblem(404, "Запрос удаления не найден.");
        }
    }
    [HttpPost("{id:guid}/resend"), EnableRateLimiting(Constants.RateLimitPolicies.RequestCode)]
    public async Task<IActionResult> Resend(Guid id, CancellationToken ct)
    {
        await Check(id, ct);
        try { return Accepted(await authentication.ResendAsync(id, Binding, ct)); }
        catch (EmailDeliveryException) { return Problem(statusCode: 503, title: "Не удалось отправить код удаления."); }
    }
    [HttpGet("{id:guid}/delivery")]
    public async Task<IActionResult> Delivery(Guid id, CancellationToken ct)
    {
        await Check(id, ct); return Ok(await authentication.DeliveryAsync(id, Binding, ct));
    }
    [HttpGet("{id:guid}/development")]
    public async Task<IActionResult> Development(Guid id, CancellationToken ct)
    {
        if (options.Value.EmailProvider != Constants.EmailProviders.Development || !OtpSecurity.IsLocalDevelopment(HttpContext, environment))
        {
            return NotFound();
        }
        await Check(id, ct);
        var message = messages.FindEmail(id, security.Hash(Constants.BrowserBindingPurpose, Binding));
        return message is null ? NotFound() : Ok(new { message.Code });
    }
    [HttpPost("confirm"), EnableRateLimiting(Constants.RateLimitPolicies.VerifyCode)]
    public async Task<IActionResult> Confirm(VerifyEmailCode request, CancellationToken ct)
    {
        await Check(request.ChallengeId, ct);
        await deletion.DeleteAsync(Session, request, ct);
        await HttpContext.SignOutAsync(Constants.AuthenticationScheme);
        HttpContext.Items.Remove(typeof(AuthSession));
        return NoContent();
    }
}
