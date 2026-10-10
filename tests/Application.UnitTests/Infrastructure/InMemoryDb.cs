using delivery_schedule_management.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace delivery_schedule_management.Application.UnitTests.Infrastructure;

/// <summary>
/// In-process EF InMemory database (no Docker, no native libraries) that builds the REAL EF model (all entity
/// configurations) so repositories, interceptors and the seeder can be unit tested. Relational behaviour
/// (unique indexes, Npgsql mapping, migrations) is covered by the Docker-based integration tests.
/// </summary>
public sealed class InMemoryDb : IDisposable
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly ISaveChangesInterceptor[] _interceptors;

    public InMemoryDb(params ISaveChangesInterceptor[] interceptors) => _interceptors = interceptors;

    public ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_name)
            .AddInterceptors(_interceptors)
            .Options;

        return new ApplicationDbContext(options);
    }

    public void Dispose()
    {
        // Nothing to release: the named in-memory store is dropped with the process.
    }
}
