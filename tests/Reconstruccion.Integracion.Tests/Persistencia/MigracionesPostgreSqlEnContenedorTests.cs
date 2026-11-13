using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class MigracionesPostgreSqlEnContenedorTests
    : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16.13")
            .Build();

    private DbContextOptions<ReconstruccionDbContext> Opciones =>
        new DbContextOptionsBuilder<ReconstruccionDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    public async Task InitializeAsync()
    {
        // Un PostgreSQL nuevo y aislado para esta clase.
        await _postgres.StartAsync();

        // Reproduce el esquema real mediante migraciones.
        await using var db = new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        // Destruye el contenedor al terminar las pruebas.
        return _postgres.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task AplicaMigracionesYConservaIndicesOrdenados()
    {
        await using var db = new ReconstruccionDbContext(Opciones);

        // Debe existir la ultima migracion del Outbox.
        var migraciones = await db.Database
            .GetAppliedMigrationsAsync();

        Assert.Contains(
            "20261113134618_OrdenOutboxPorDesastre",
            migraciones);

        // La base inicia sin datos de negocio.
        Assert.False(await db.Planes.AnyAsync());
        Assert.False(await db.Proyectos.AnyAsync());
        Assert.False(await db.EventosOutbox.AnyAsync());

        // Comprobar los indices reales de PostgreSQL.
        await using var conexion = new NpgsqlConnection(
            _postgres.GetConnectionString());

        await conexion.OpenAsync();

        const string sql = """
            SELECT
                (
                    SELECT count(*)
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'eventos_outbox'
                      AND indexname IN (
                        'IX_eventos_outbox_DesastreId_Secuencia',
                        'IX_eventos_outbox_PublicadoEn_DesastreId_Secuencia'
                      )
                ),
                (
                    SELECT count(*)
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'eventos_outbox'
                      AND indexdef LIKE '%"VersionAgregado"%'
                ),
                (
                    SELECT count(*)
                    FROM pg_indexes
                    WHERE schemaname = 'public'
                      AND tablename = 'eventos_outbox'
                      AND indexname = 'IX_eventos_outbox_Secuencia'
                      AND indexdef LIKE 'CREATE UNIQUE INDEX%'
                );
            """;

        await using var comando = new NpgsqlCommand(sql, conexion);

        await using var lector = await comando.ExecuteReaderAsync();

        Assert.True(await lector.ReadAsync());

        // Estan presentes los dos indices nuevos.
        Assert.Equal(2L, lector.GetInt64(0));

        // Ningun indice ordena por VersionAgregado.
        Assert.Equal(0L, lector.GetInt64(1));

        // Se mantiene la unicidad global de Secuencia.
        Assert.Equal(1L, lector.GetInt64(2));
    }
}