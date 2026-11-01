using System.Security.Claims;
using BikeHub.Api.Authentication;
using BikeHub.Api.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace BikeHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AuthController(EmailAuthenticationService authentication, IAuthenticationStore store,
    ICurrentUser current, IAntiforgery antiforgery, OtpSecurity security, DevelopmentMessages messages,
    IHostEnvironment environment, IOptions<AuthOptions> configuration, MockCommunityStore community) : ControllerBase
{
    private string BindingCookie => environment.IsDevelopment() ? "BikeHub.Challenge" : "__Host-BikeHub.Challenge";
    private bool DevelopmentMailbox => configuration.Value.Enabled && configuration.Value.EmailProvider == "Development" &&
                                       OtpSecurity.IsLocalDevelopment(HttpContext, environment);

    [HttpGet("session")]
    public ActionResult<AuthBootstrap> Session() => Ok(Bootstrap());

    [HttpPost("email/challenges")]
    [EnableRateLimiting("auth-request")]
    public async Task<ActionResult<EmailCodeResponse>> RequestCode(RequestEmailCode request, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Enabled) return Problem(statusCode: 503, title: "Вход пока недоступен.");
        if (configuration.Value.EmailProvider == "Development" && !DevelopmentMailbox) return NotFound();
        var email = OtpSecurity.NormalizeEmail(request.Email);
        if (email is null) return Problem(statusCode: 400, title: "Укажите корректный адрес почты.");
        var name = request.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && name.Length < 2)
            return Problem(statusCode: 400, title: "Имя должно содержать хотя бы два символа.");
        var binding = Request.Cookies[BindingCookie];
        if (binding is null || binding.Length != 64 || !binding.All(Uri.IsHexDigit)) binding = OtpSecurity.NewBinding();
        Response.Cookies.Append(BindingCookie, binding, new CookieOptions
        {
            HttpOnly = true, Secure = !environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Strict, Path = "/", MaxAge = TimeSpan.FromMinutes(15), IsEssential = true
        });
        try
        {
            var result = await authentication.RequestAsync(email, name, binding, cancellationToken);
            if (result is null)
            {
                Response.Headers.RetryAfter = configuration.Value.ResendSeconds.ToString();
                return Problem(statusCode: 429, title: "Слишком много запросов. Попробуйте позже.");
            }
            return Accepted(result);
        }
        catch (EmailDeliveryException)
        {
            return Problem(statusCode: 503, title: "Не удалось отправить письмо. Попробуйте позже.");
        }
    }

    [HttpPost("email/verify")]
    [EnableRateLimiting("auth-verify")]
    public async Task<ActionResult<AuthBootstrap>> Verify(VerifyEmailCode request, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Enabled) return Problem(statusCode: 503, title: "Вход пока недоступен.");
        if (configuration.Value.EmailProvider == "Development" && !DevelopmentMailbox) return NotFound();
        var binding = Request.Cookies[BindingCookie];
        if (string.IsNullOrEmpty(binding) || binding.Length != 64) return InvalidCode();
        var result = await authentication.VerifyAsync(request.ChallengeId, request.Code, binding, cancellationToken);
        if (result.Failure == ConfirmationFailure.TooManyAttempts)
            return Problem(statusCode: 429, title: "Попытки закончились. Запросите новый код.");
        if (result.Session is not { } session) return InvalidCode();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.Account.Id.ToString()),
            new Claim(ClaimTypes.Name, session.Account.DisplayName),
            new Claim("session_id", session.Id.ToString()),
            new Claim("email_verified", "true")
        }, AuthenticationExtensions.Scheme));
        await HttpContext.SignInAsync(AuthenticationExtensions.Scheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
        HttpContext.User = principal;
        HttpContext.Items[typeof(AuthSession)] = session;
        Response.Cookies.Delete(BindingCookie, new CookieOptions { Path = "/", Secure = !environment.IsDevelopment() });
        return Ok(Bootstrap());
    }

    [HttpPost("logout")]
    public async Task<ActionResult<AuthBootstrap>> Logout(CancellationToken cancellationToken)
    {
        if (HttpContext.Items[typeof(AuthSession)] is AuthSession session)
            await store.RevokeSessionAsync(session.Id, cancellationToken);
        await HttpContext.SignOutAsync(AuthenticationExtensions.Scheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        HttpContext.Items.Remove(typeof(AuthSession));
        AuthTelemetry.Record("signed_out");
        return Ok(Bootstrap());
    }

    [HttpGet("development/messages/{id:guid}")]
    public IActionResult DevelopmentMessage(Guid id)
    {
        if (!DevelopmentMailbox || Request.Cookies[BindingCookie] is not { Length: 64 } binding) return NotFound();
        var message = messages.FindEmail(id, security.Hash("browser", binding));
        return message is null ? NotFound() : Ok(new
        {
            subject = "Код для входа в BikeHub", text = $"Ваш код: {message.Code}", message.Code, message.ExpiresAt
        });
    }

    private AuthBootstrap Bootstrap()
    {
        var account = current.Account;
        var user = account is null ? null : community.EnsureUser(account.Id, account.DisplayName);
        var at = account?.Email.IndexOf('@') ?? 0;
        var masked = account is null ? null : account.Email[..1] + "***" + account.Email[at..];
        return new(user, account is not null, masked, false,
            antiforgery.GetAndStoreTokens(HttpContext).RequestToken!, configuration.Value.Enabled, DevelopmentMailbox);
    }

    private ObjectResult InvalidCode() => Problem(statusCode: 400, title: "Код неверный или истёк. Проверьте письмо или запросите новый код.");
}
