namespace Merfit.Application.Common.Interfaces;

/// <summary>Resolves the authenticated caller from the current HTTP context. Implemented in the Api layer (reads ClaimsPrincipal) so Application stays free of ASP.NET Core dependencies.</summary>
public interface ICurrentUserService
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsInRole(string role);
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
    string? UserAgent { get; }
}
