using System.Net;
using System.Net.Http.Json;
using BikeHub.Api.Authentication;
using BikeHub.Application.Community;
using Microsoft.Extensions.DependencyInjection;

namespace BikeHub.Api.Tests;

public sealed class EmailAuthenticationTests
{
    [Fact]
    public async Task Guest_can_read_but_cannot_modify_or_read_personal_data()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/feed")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/feed?tab=saved")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/api/bikes", new { })).StatusCode);
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        Assert.Null(session!.User);
        Assert.False(session.EmailConfirmed);
        Assert.True(session.DevelopmentMailbox);
    }

    [Fact]
    public async Task Email_code_creates_a_real_session_and_cookie_is_http_only()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        using var diagnostic = await browser.GetAsync("/api/auth/session");
        Assert.True(diagnostic.IsSuccessStatusCode, string.Join("\n", app.Logs.Entries));
        var (session, response) = await AuthTestClient.Login(browser, "alice@example.test");
        using (response)
        {
            Assert.NotNull(session.User);
            Assert.True(session.EmailConfirmed);
            Assert.False(session.PhoneConfirmed);
            Assert.Equal("a***@example.test", session.MaskedEmail);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), value => value.StartsWith("BikeHub.Auth=") && value.Contains("httponly", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(session.User.Id, (await browser.GetFromJsonAsync<UserDto>("/api/users/me"))!.Id);
            Assert.NotEqual(Guid.Parse("10000000-0000-0000-0000-000000000001"), session.User.Id);
        }
    }

    [Fact]
    public async Task Same_email_reuses_the_account_and_codes_are_single_use()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (challenge, code) = await AuthTestClient.Challenge(browser, "Alice@EXAMPLE.test");
        using var first = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        var original = (await first.Content.ReadFromJsonAsync<AuthBootstrap>())!.User!.Id;
        using var replay = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        using var logout = await AuthTestClient.Post(browser, "/api/auth/logout");
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        var (next, response) = await AuthTestClient.Login(browser, "alice@example.test");
        using (response)
        {
            Assert.Equal(original, next.User!.Id);
        }
    }

    [Fact]
    public async Task Code_cannot_be_read_or_used_from_another_browser()
    {
        await using var app = new AuthFactory();
        using var owner = app.Browser();
        using var stranger = app.Browser();
        var (challenge, code) = await AuthTestClient.Challenge(owner, "private@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync("/api/auth/development/messages/" + challenge.ChallengeId)).StatusCode);
        using var stolen = await AuthTestClient.Post(stranger, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode);
        using var valid = await AuthTestClient.Post(owner, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Invalid_attempts_are_limited_even_if_the_correct_code_is_submitted_later()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (challenge, code) = await AuthTestClient.Challenge(browser, "limited@example.test");
        var invalid = code == "000000" ? "111111" : "000000";
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var result = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code = invalid });
            Assert.Equal(attempt == 4 ? HttpStatusCode.TooManyRequests : HttpStatusCode.BadRequest, result.StatusCode);
        }
        using var final = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.TooManyRequests, final.StatusCode);
    }

    [Fact]
    public async Task Expired_codes_and_sessions_are_rejected()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (challenge, code) = await AuthTestClient.Challenge(browser, "expiry@example.test");
        app.Clock.Advance(TimeSpan.FromMinutes(6));
        using var expired = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = challenge.ChallengeId, code });
        Assert.Equal(HttpStatusCode.BadRequest, expired.StatusCode);
        var (_, response) = await AuthTestClient.Login(browser, "expiry@example.test");
        response.Dispose();
        app.Clock.Advance(TimeSpan.FromDays(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Resend_enforces_cooldown_and_invalidates_previous_code()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (old, code) = await AuthTestClient.Challenge(browser, "resend@example.test");
        using var tooSoon = await AuthTestClient.Post(browser, "/api/auth/email/challenges", new { email = "RESEND@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, tooSoon.StatusCode);
        app.Clock.Advance(TimeSpan.FromSeconds(61));
        var (fresh, freshCode) = await AuthTestClient.Challenge(browser, "resend@example.test");
        using var oldResult = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = old.ChallengeId, code });
        Assert.Equal(HttpStatusCode.BadRequest, oldResult.StatusCode);
        using var good = await AuthTestClient.Post(browser, "/api/auth/email/verify", new { challengeId = fresh.ChallengeId, code = freshCode });
        Assert.Equal(HttpStatusCode.OK, good.StatusCode);
    }

    [Fact]
    public async Task Requests_without_csrf_protection_are_rejected()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        using var request = await AuthTestClient.Post(browser, "/api/auth/email/challenges", new { email = "csrf@example.test" }, csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, request.StatusCode);
        var (_, response) = await AuthTestClient.Login(browser, "csrf@example.test");
        response.Dispose();
        using var logout = await AuthTestClient.Post(browser, "/api/auth/logout", csrf: false);
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_even_if_the_old_cookie_is_replayed()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (_, response) = await AuthTestClient.Login(browser, "logout@example.test");
        var cookie = response.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("BikeHub.Auth=")).Split(';')[0];
        response.Dispose();
        using var logout = await AuthTestClient.Post(browser, "/api/auth/logout");
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var replay = app.Browser();
        replay.DefaultRequestHeaders.Add("Cookie", cookie);
        Assert.Equal(HttpStatusCode.Unauthorized, (await replay.GetAsync("/api/users/me")).StatusCode);
    }

    [Fact]
    public async Task Reactions_and_ownership_are_isolated_between_accounts()
    {
        await using var app = new AuthFactory();
        using var alice = app.Browser();
        using var bob = app.Browser();
        var (a, aResponse) = await AuthTestClient.Login(alice, "alice@example.test"); aResponse.Dispose();
        var (b, bResponse) = await AuthTestClient.Login(bob, "bob@example.test"); bResponse.Dispose();
        Assert.NotEqual(a.User!.Id, b.User!.Id);
        const string post = "/api/posts/30000000-0000-0000-0000-000000000001";
        var bootstrap = (await alice.GetFromJsonAsync<AuthBootstrap>("/api/auth/session"))!;
        using var like = new HttpRequestMessage(HttpMethod.Put, post + "/like");
        like.Headers.Add("X-CSRF-TOKEN", bootstrap.CsrfToken);
        using var liked = await alice.SendAsync(like);
        Assert.Equal(HttpStatusCode.OK, liked.StatusCode);
        Assert.True((await alice.GetFromJsonAsync<PostDto>(post))!.IsLiked);
        Assert.False((await bob.GetFromJsonAsync<PostDto>(post))!.IsLiked);
        using var created = await AuthTestClient.Post(alice, "/api/bikes", new { name = "Alice Gravel", brand = "Canyon", category = "gravel", year = 2024, mileage = 0, description = "" });
        var bike = (await created.Content.ReadFromJsonAsync<BikeDto>())!;
        Assert.Equal(a.User.Id, bike.OwnerId);
        using var forbidden = await AuthTestClient.Post(bob, "/api/posts", new { bikeId = bike.Id, title = "Чужой велосипед", content = "Нельзя публиковать от имени другого пользователя.", category = "story" });
        Assert.Equal(HttpStatusCode.BadRequest, forbidden.StatusCode);
    }

    [Fact]
    public async Task Development_mailbox_is_unavailable_to_non_loopback_clients()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var (challenge, _) = await AuthTestClient.Challenge(browser, "local@example.test");
        browser.DefaultRequestHeaders.Add("X-Test-Remote", "remote");
        browser.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
        Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/auth/development/messages/" + challenge.ChallengeId)).StatusCode);
        using var request = await AuthTestClient.Post(browser, "/api/auth/email/challenges", new { email = "remote@example.test" });
        Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
    }

    [Fact]
    public async Task Smtp_errors_do_not_expose_addresses_or_codes_in_logs_or_responses()
    {
        var sender = new FailingSender();
        await using var app = new AuthFactory { Sender = sender };
        using var browser = app.Browser();
        using var response = await AuthTestClient.Post(browser, "/api/auth/email/challenges", new { email = "private@example.test" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var logs = string.Join('\n', app.Logs.Entries);
        Assert.DoesNotContain("private@example.test", logs);
        Assert.DoesNotContain(sender.Code!, logs);
        Assert.DoesNotContain("private@example.test", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Test_sms_provider_records_messages_without_sending_them()
    {
        await using var app = new AuthFactory();
        using var browser = app.Browser();
        var id = Guid.NewGuid();
        await app.Services.GetRequiredService<ISmsSender>().SendAsync(
            new(id, "+79990000000", "123456", new string('A', 64), app.Clock.GetUtcNow().AddMinutes(5), 5), default);
        Assert.Equal("sms", app.Services.GetRequiredService<DevelopmentMessages>().FindForTesting(id)!.Channel);
    }

    [Fact]
    public void Production_cannot_start_with_development_authentication()
    {
        using var app = new AuthFactory { EnvironmentName = "Production" };
        var exception = Assert.ThrowsAny<Exception>(() => app.Browser());
        Assert.Contains("Production authentication requires", exception.ToString());
    }

    [Fact]
    public async Task Production_issues_secure_cookies_and_has_no_development_mailbox()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bikehub-auth-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var app = new AuthFactory { EnvironmentName = "Production" };
            app.Settings["Authentication:Store"] = "Postgres";
            app.Settings["ConnectionStrings:BikeHub"] = "Host=127.0.0.1;Port=1;Database=unused;Username=unused";
            app.Settings["Authentication:EmailProvider"] = "MailService";
            app.Settings["Authentication:OtpSecret"] = new string('A', 64);
            app.Settings["Authentication:DataProtectionPath"] = directory;
            app.Settings["MailService:BaseUrl"] = "http://mail.example.test/";
            app.Settings["MailService:ApiKey"] = new string('M', 32);
            app.Settings["ReverseProxy:KnownProxies:0"] = "127.0.0.1";
            using var browser = app.Browser(https: true);
            using var response = await browser.GetAsync("/api/auth/session");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False((await response.Content.ReadFromJsonAsync<AuthBootstrap>())!.DevelopmentMailbox);
            Assert.Contains(response.Headers.GetValues("Set-Cookie"), cookie =>
                cookie.StartsWith("__Host-BikeHub.Csrf=") && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
                cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(HttpStatusCode.NotFound, (await browser.GetAsync("/api/auth/development/messages/" + Guid.NewGuid())).StatusCode);
            using var proxy = app.Browser();
            proxy.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            Assert.Equal(HttpStatusCode.OK, (await proxy.GetAsync("/api/auth/session")).StatusCode);
            proxy.DefaultRequestHeaders.Add("X-Test-Remote", "untrusted");
            Assert.Equal(HttpStatusCode.InternalServerError, (await proxy.GetAsync("/api/auth/session")).StatusCode);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Disabled_authentication_keeps_guest_bootstrap_available_over_http()
    {
        await using var app = new AuthFactory { EnvironmentName = "Production" };
        app.Settings["Authentication:Enabled"] = "false";
        using var browser = app.Browser();
        var session = await browser.GetFromJsonAsync<AuthBootstrap>("/api/auth/session");
        Assert.False(session!.Enabled);
        Assert.Null(session.User);
    }

    private sealed class FailingSender : IVerificationEmailSender
    {
        public string? Code { get; private set; }
        public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
        {
            Code = message.Code;
            throw new InvalidOperationException("Private SMTP diagnostic " + message.Destination + " " + message.Code);
        }
    }
}
