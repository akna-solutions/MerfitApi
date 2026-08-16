using Merfit.Application.Features.Auth;
using Merfit.Domain.Entities.Identity;
using Merfit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merfit.Infrastructure.Repositories;

public sealed class RefreshTokenRepository(MerfitDbContext db) : IRefreshTokenRepository
{
    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens.AddAsync(token, cancellationToken);

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyCollection<RefreshToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(cancellationToken);

    public void Update(RefreshToken token) => db.RefreshTokens.Update(token);
}
