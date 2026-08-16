using Merfit.Application.Common.Interfaces;
using Merfit.Domain.Entities.Identity;
using Microsoft.AspNetCore.Identity;

namespace Merfit.Infrastructure.Identity;

/// <summary>
/// Wraps ASP.NET Core Identity's PasswordHasher&lt;T&gt; (PBKDF2, per-password random salt,
/// versioned format that supports transparent rehash-on-verify when the work factor changes).
/// The generic type parameter is unused beyond satisfying the API — no other Identity machinery
/// (UserManager, IdentityDbContext, etc.) is pulled in.
/// </summary>
public sealed class PasswordHasherService : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string hashedPassword, string providedPassword, out bool needsRehash)
    {
        var result = _inner.VerifyHashedPassword(null!, hashedPassword, providedPassword);
        needsRehash = result == PasswordVerificationResult.SuccessRehashNeeded;
        return result != PasswordVerificationResult.Failed;
    }
}
