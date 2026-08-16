namespace Merfit.Application.Common.Interfaces;

/// <summary>
/// Single source of truth for "is this user Plus or Free" — always resolved server-side from the
/// Subscriptions store, never trusted from client input or blindly from JWT claims. Implemented by
/// the Subscriptions feature; consumed by Auth (JWT claim hint) and by IEntitlementService.
/// </summary>
public interface IMembershipProvider
{
    Task<string> GetMembershipAsync(Guid userId, CancellationToken cancellationToken = default);
}
