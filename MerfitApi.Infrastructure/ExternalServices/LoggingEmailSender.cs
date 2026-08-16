using Merfit.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace Merfit.Infrastructure.ExternalServices;

/// <summary>
/// MVP email "provider": logs the message instead of sending it. Swapping in SendGrid/SES means
/// implementing IEmailSender and changing one DI registration — Application code is untouched.
/// Never logs anything beyond subject/recipient at Information level in Production (body is Debug-only)
/// to avoid leaking token values into aggregated logs at default verbosity.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Email queued (no provider configured): to={ToEmail} subject={Subject}", toEmail, subject);
        logger.LogDebug("Email body: {Body}", htmlBody);
        return Task.CompletedTask;
    }
}
