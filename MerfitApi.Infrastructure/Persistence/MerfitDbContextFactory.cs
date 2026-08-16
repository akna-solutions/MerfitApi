using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Merfit.Infrastructure.Persistence;

/// <summary>
/// Enables `dotnet ef migrations add` / `dotnet ef database update` to be run from the CLI
/// without spinning up the full Api host. Reads the same appsettings.json used by the Api
/// project (ConnectionStrings:DefaultConnection), falling back to a local-dev default.
/// </summary>
public sealed class MerfitDbContextFactory : IDesignTimeDbContextFactory<MerfitDbContext>
{
    public MerfitDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "..", "MerfitApi.Api"))
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=merfit;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<MerfitDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(MerfitDbContext).Assembly.FullName));

        return new MerfitDbContext(optionsBuilder.Options);
    }
}
