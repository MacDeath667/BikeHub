using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using BikeHub.Mail.Contracts;

namespace BikeHub.Api.Authentication;

public sealed class MailServiceOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class MailServiceEmailSender(HttpClient client) : IVerificationEmailSender
{
    private const string ApiKeyHeader = "X-Api-Key";
    private const string IdempotencyHeader = "Idempotency-Key";
    private const string BodyTemplate = "Код для входа в BikeHub: {0}\n\n" +
        "Код действует {1} минут и подходит только для того браузера, где вы запросили вход.\n\n" +
        "Если вы не запрашивали вход, проигнорируйте это письмо.";
    private const string SubmissionFailed = "Mail service did not accept the verification email.";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static void Configure(HttpClient client, MailServiceOptions options)
    {
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add(ApiKeyHeader, options.ApiKey);
    }

    public async Task SendAsync(VerificationMessage message, CancellationToken cancellationToken)
    {
        var email = new SendEmailRequest(message.ChallengeId, message.Destination, Constants.VerificationEmailSubject,
            string.Format(BodyTemplate, message.Code, message.ValidMinutes), message.ExpiresAt);
        // The challenge ID remains stable if the caller retries an uncertain HTTP submission.
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/emails") { Content = JsonContent.Create(email) };
                request.Headers.Add(IdempotencyHeader, message.ChallengeId.ToString());
                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(SubmissionFailed);
                }
                var receipt = await response.Content.ReadFromJsonAsync<EmailStatus>(JsonOptions, cancellationToken);
                if (receipt?.MessageId != message.ChallengeId || receipt.Status is not
                    (DeliveryStatus.Queued or DeliveryStatus.Sending or DeliveryStatus.Sent))
                {
                    throw new EmailDeliveryException();
                }
                return;
            }
            catch (HttpRequestException) when (attempt < 2 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
            }
        }
    }
}
