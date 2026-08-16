namespace Merfit.Application.Common.Interfaces;

/// <summary>Provider-agnostic email abstraction. MVP ships a logging-only implementation; SendGrid/SES can be swapped in later without touching Application code.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default);
}
