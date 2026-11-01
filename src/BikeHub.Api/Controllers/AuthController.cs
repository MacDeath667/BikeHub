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
    private const string ChallengeCookieName = "BikeHub.Challenge";
    private const string SecureChallengeCookieName = "__Host-BikeHub.Challenge";
    private const string InvalidEmailTitle = "Укажите корректный адрес почты.";
    private const string ShortNameTitle = "Имя должно содержать хотя бы два символа.";
    private const string ThrottledTitle = "Слишком много запросов. Попробуйте позже.";
    private const string DeliveryFailedTitle = "Не удалось отправить письмо. Попробуйте позже.";
    private const string AttemptsExceededTitle = "Попытки закончились. Запросите новый код.";
    private const string InvalidCodeTitle = "Код неверный или истёк. Проверьте письмо или запросите новый код.";
    private const string SignedOutEvent = "signed_out";
    private const string EmailMask = "***";
    private const string DevelopmentCodeTemplate = "Ваш код: {0}";

    private string BindingCookie => environment.IsDevelopment() ? ChallengeCookieName : SecureChallengeCookieName;
    private bool DevelopmentMailbox => configuration.Value.Enabled && configuration.Value.EmailProvider == Constants.EmailProviders.Development &&
                                       OtpSecurity.IsLocalDevelopment(HttpContext, environment);

    [HttpGet("session")]
    public ActionResult<AuthBootstrap> Session() => Ok(Bootstrap());

    [HttpPost("email/challenges")]
    [EnableRateLimiting(Constants.RateLimitPolicies.RequestCode)]
    public async Task<ActionResult<EmailCodeResponse>> RequestCode(RequestEmailCode request, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Enabled)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Constants.AuthenticationUnavailableTitle);
        }

        if (configuration.Value.EmailProvider == Constants.EmailProviders.Development && !DevelopmentMailbox)
        {
            return NotFound();
        }

        var email = OtpSecurity.NormalizeEmail(request.Email);
        if (email is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidEmailTitle);
        }

        var name = request.Name?.Trim();
        if (!string.IsNullOrEmpty(name) && name.Length < Constants.MinimumDisplayNameLength)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ShortNameTitle);
        }

        var binding = Request.Cookies[BindingCookie];
        if (binding is null || binding.Length != Constants.BrowserBindingLength || !binding.All(Uri.IsHexDigit))
        {
            binding = OtpSecurity.NewBinding();
        }

        Response.Cookies.Append(BindingCookie, binding, new CookieOptions
        {
            HttpOnly = true,
            Secure = !environment.IsDevelopment() || Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = Constants.CookiePath,
            MaxAge = TimeSpan.FromMinutes(15),
            IsEssential = true
        });
        try
        {
            var result = await authentication.RequestAsync(email, name, binding, cancellationToken);
            if (result is null)
            {
                Response.Headers.RetryAfter = configuration.Value.ResendSeconds.ToString();
                return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ThrottledTitle);
            }
            return Accepted(result);
        }
        catch (EmailDeliveryException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: DeliveryFailedTitle);
        }
    }

    [HttpPost("email/verify")]
    [EnableRateLimiting(Constants.RateLimitPolicies.VerifyCode)]
    public async Task<ActionResult<AuthBootstrap>> Verify(VerifyEmailCode request, CancellationToken cancellationToken)
    {
        if (!configuration.Value.Enabled)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Constants.AuthenticationUnavailableTitle);
        }

        if (configuration.Value.EmailProvider == Constants.EmailProviders.Development && !DevelopmentMailbox)
        {
            return NotFound();
        }

        var binding = Request.Cookies[BindingCookie];
        if (string.IsNullOrEmpty(binding) || binding.Length != Constants.BrowserBindingLength)
        {
            return InvalidCode();
        }

        var result = await authentication.VerifyAsync(request.ChallengeId, request.Code, binding, cancellationToken);
        if (result.Failure == ConfirmationFailure.TooManyAttempts)
        {
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, title: AttemptsExceededTitle);
        }

        if (result.Session is not { } session)
        {
            return InvalidCode();
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.Account.Id.ToString()),
            new Claim(ClaimTypes.Name, session.Account.DisplayName),
            new Claim(Constants.Claims.SessionId, session.Id.ToString()),
            new Claim(Constants.Claims.EmailVerified, Constants.Claims.VerifiedValue)
        }, Constants.AuthenticationScheme));
        await HttpContext.SignInAsync(Constants.AuthenticationScheme, principal,
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = session.ExpiresAt, AllowRefresh = false });
        HttpContext.User = principal;
        HttpContext.Items[typeof(AuthSession)] = session;
        Response.Cookies.Delete(BindingCookie, new CookieOptions { Path = Constants.CookiePath, Secure = !environment.IsDevelopment() });
        return Ok(Bootstrap());
    }

    [HttpPost("logout")]
    public async Task<ActionResult<AuthBootstrap>> Logout(CancellationToken cancellationToken)
    {
        if (HttpContext.Items[typeof(AuthSession)] is AuthSession session)
        {
            await store.RevokeSessionAsync(session.Id, cancellationToken);
        }

        await HttpContext.SignOutAsync(Constants.AuthenticationScheme);
        HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
        HttpContext.Items.Remove(typeof(AuthSession));
        AuthTelemetry.Record(SignedOutEvent);
        return Ok(Bootstrap());
    }

    [HttpGet("development/messages/{id:guid}")]
    public IActionResult DevelopmentMessage(Guid id)
    {
        if (!DevelopmentMailbox || Request.Cookies[BindingCookie] is not { Length: Constants.BrowserBindingLength } binding)
        {
            return NotFound();
        }

        var message = messages.FindEmail(id, security.Hash(Constants.BrowserBindingPurpose, binding));
        return message is null ? NotFound() : Ok(new
        {
            subject = Constants.VerificationEmailSubject,
            text = string.Format(DevelopmentCodeTemplate, message.Code),
            message.Code,
            message.ExpiresAt
        });
    }

    private AuthBootstrap Bootstrap()
    {
        var account = current.Account;
        var user = account is null ? null : community.EnsureUser(account.Id, account.DisplayName);
        var at = account?.Email.IndexOf('@') ?? 0;
        var masked = account is null ? null : account.Email[..1] + EmailMask + account.Email[at..];
        return new(user, account is not null, masked, false,
            antiforgery.GetAndStoreTokens(HttpContext).RequestToken!, configuration.Value.Enabled, DevelopmentMailbox);
    }

    private ObjectResult InvalidCode() => Problem(statusCode: StatusCodes.Status400BadRequest, title: InvalidCodeTitle);
}
