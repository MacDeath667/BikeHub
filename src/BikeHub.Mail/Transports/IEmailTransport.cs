using BikeHub.Mail.Contracts;

namespace BikeHub.Mail.Transports;

public interface IEmailTransport
{
    Task SendAsync(SendEmailRequest message, CancellationToken cancellationToken);
}

public sealed class DevelopmentEmailTransport(IHostEnvironment environment) : IEmailTransport
{
    private const string RestrictionMessage = "Development transport is restricted to Development.";

    public Task SendAsync(SendEmailRequest message, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException(RestrictionMessage);
        }
        // A dry run: never send, log, or expose an OTP to an unauthenticated reader.
        return Task.CompletedTask;
    }
}
