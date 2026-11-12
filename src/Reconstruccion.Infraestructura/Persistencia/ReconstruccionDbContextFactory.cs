using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Reconstruccion.Infraestructura.Persistencia;

public sealed class ReconstruccionDbContextFactory
    : IDesignTimeDbContextFactory<ReconstruccionDbContext>
{
    public ReconstruccionDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(
            "RECONSTRUCCION_DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Falta la variable RECONSTRUCCION_DB_CONNECTION.");
        }

        var options = new DbContextOptionsBuilder<ReconstruccionDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ReconstruccionDbContext(options);
    }
}