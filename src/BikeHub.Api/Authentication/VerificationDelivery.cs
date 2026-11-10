using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using BikeHub.Mail.Contracts;

namespace BikeHub.Api.Authentication;

public sealed class VerificationPayload(IDataProtectionProvider provider)
{
    private const string ProtectionPurpose = "BikeHub.VerificationMessage.v1";
    private readonly IDataProtector protector = provider.CreateProtector(ProtectionPurpose);
    public string Protect(VerificationMessage message) => protector.Protect(JsonSerializer.Serialize(message));
    public VerificationMessage Read(EmailChallenge challenge)
    {
        var message = JsonSerializer.Deserialize<VerificationMessage>(protector.Unprotect(challenge.ProtectedMessage!))!;
        if (message.ChallengeId != challenge.Id || message.Destination != challenge.Email || message.BindingHash != challenge.BindingHash)
        {
            throw new EmailDeliveryException();
        }
        return message;
    }
}

public static class VerificationEmail
{
    private const string BodyTemplate = "Код для входа в BikeHub: {0}\n\n" +
        "Код действует {1} минут и подходит только для того браузера, где вы запросили вход.\n\n" +
        "Если вы не запрашивали вход, проигнорируйте это письмо.";
    public static SendEmailRequest Create(VerificationMessage message) => new(message.ChallengeId,
        message.Destination, Constants.VerificationEmailSubject,
        string.Format(BodyTemplate, message.Code, message.ValidMinutes), message.ExpiresAt);
}

public interface IVerificationDeliveryStatus
{
    Task<DeliveryStatus?> GetStatusAsync(Guid id, CancellationToken cancellationToken);
}

public sealed record VerificationDeliveryStatus(string Status);
