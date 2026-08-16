using Merfit.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace Merfit.Infrastructure.Persistence;

public sealed class UnitOfWork(MerfitDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is not null)
        {
            // Already inside a transaction (nested call) — just run the work, the outer scope owns commit/rollback.
            await work(cancellationToken);
            return;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await work(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
