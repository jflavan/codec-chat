namespace Codec.Api.Services;

public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string to, string subject, string htmlBody)
    {
        logger.LogInformation(
            "═══ EMAIL ═══\nTo: {To}\nSubject: {Subject}\nBody:\n{Body}\n═════════════",
            to, subject, htmlBody);
        return Task.CompletedTask;
    }
}
