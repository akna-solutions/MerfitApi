using Merfit.Application.Features.Auth;
using Merfit.Domain.Entities.Identity;
using Merfit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Merfit.Infrastructure.Repositories;

public sealed class UserTokenRepository(MerfitDbContext db) : IUserTokenRepository
{
    public async Task AddAsync(UserToken token, CancellationToken cancellationToken = default) =>
        await db.UserTokens.AddAsync(token, cancellationToken);

    public Task<UserToken?> GetByHashAsync(string tokenHash, UserTokenPurpose purpose, CancellationToken cancellationToken = default) =>
        db.UserTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash && t.Purpose == purpose, cancellationToken);

    public void Update(UserToken token) => db.UserTokens.Update(token);
}
