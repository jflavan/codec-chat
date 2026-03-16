using Azure.Communication.Email;

namespace Codec.Api.Services;

public class AzureEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        var connectionString = configuration["Email:ConnectionString"]
            ?? throw new InvalidOperationException("Email:ConnectionString is required.");
        var senderAddress = configuration["Email:SenderAddress"]
            ?? throw new InvalidOperationException("Email:SenderAddress is required.");

        var client = new EmailClient(connectionString);
        await client.SendAsync(
            Azure.WaitUntil.Completed,
            senderAddress,
            to,
            subject,
            htmlBody);
    }
}
