using System.Collections.Concurrent;

namespace BikeHub.Api.Authentication;

public sealed record DevelopmentMessage(string Channel, VerificationMessage Message);

public sealed class DevelopmentMessages(TimeProvider clock)
{
    private readonly ConcurrentDictionary<Guid, DevelopmentMessage> messages = new();

    public void Add(string channel, VerificationMessage message)
    {
        foreach (var (id, existing) in messages)
            if (existing.Message.ExpiresAt <= clock.GetUtcNow()) messages.TryRemove(id, out _);
        messages[message.ChallengeId] = new(channel, message);
    }

    public VerificationMessage? FindEmail(Guid id, string bindingHash) =>
        messages.TryGetValue(id, out var value) && value.Channel == "email" &&
        value.Message.ExpiresAt > clock.GetUtcNow() && OtpSecurity.EqualsHash(value.Message.BindingHash, bindingHash)
            ? value.Message : null;

    public DevelopmentMessage? FindForTesting(Guid id) => messages.GetValueOrDefault(id);
}

public sealed class DevelopmentEmailSender(DevelopmentMessages messages, IHostEnvironment environment) : IVerificationEmailSender
{
    public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Development email is restricted to Development.");
        messages.Add("email", message);
        return Task.CompletedTask;
    }
}

public sealed class TestSmsSender(DevelopmentMessages messages, IHostEnvironment environment) : ISmsSender
{
    public Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment()) throw new InvalidOperationException("Test SMS is restricted to Development.");
        messages.Add("sms", message);
        return Task.CompletedTask;
    }
}
