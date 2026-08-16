namespace Merfit.Application.Common.Interfaces;

public sealed record PushMessage(string ToToken, string Title, string Body, IReadOnlyDictionary<string, string>? Data = null);

public sealed record PushSendResult(string ToToken, bool Success, string? Error, bool TokenInvalid);

/// <summary>Provider-agnostic push abstraction. MVP implements ExpoPushNotificationProvider; FCM/APNS/OneSignal can be added later behind the same interface.</summary>
public interface IExternalPushProvider
{
    Task<IReadOnlyList<PushSendResult>> SendAsync(IReadOnlyList<PushMessage> messages, CancellationToken cancellationToken = default);
}
