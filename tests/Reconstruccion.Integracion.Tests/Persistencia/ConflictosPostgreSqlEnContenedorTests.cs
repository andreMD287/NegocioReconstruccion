using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Registros;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class ConflictosPostgreSqlEnContenedorTests : IAsyncLifetime
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
        await _postgres.StartAsync();

        await using var db = new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task PlanDuplicado_SeTraduceAConflicto()
    {
        await using var db = new ReconstruccionDbContext(Opciones);
        await using var transaccion =
            await db.Database.BeginTransactionAsync();

        var desastreId = Guid.NewGuid();

        var primero = PlanReconstruccion.Abrir(
            Guid.NewGuid(),
            desastreId);

        new RepositorioPlanesEf(db).Agregar(primero);

        await new UnidadDeTrabajoEf(db).ConfirmarAsync();

        // Distinto PlanId, pero mismo DesastreId.
        var segundo = PlanReconstruccion.Abrir(
            Guid.NewGuid(),
            desastreId);

        new RepositorioPlanesEf(db).Agregar(segundo);

        var conflicto = await Assert.ThrowsAsync<
            ConflictoDeVersionException>(
                () => new UnidadDeTrabajoEf(db).ConfirmarAsync());

        var errorEf = Assert.IsType<DbUpdateException>(
            conflicto.InnerException);

        var postgres = Assert.IsType<PostgresException>(
            errorEf.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgres.SqlState);

        Assert.Equal(
            "IX_planes_reconstruccion_DesastreId",
            postgres.ConstraintName);

        // El segundo Plan no se guardo parcialmente.
        var cantidad = await db.Planes
            .AsNoTracking()
            .CountAsync(x => x.DesastreId == desastreId);

        Assert.Equal(1, cantidad);

        // La base queda vacia para las demas pruebas.
        await transaccion.RollbackAsync();
    }

    [Fact]
    public async Task RequestIdDuplicado_SeTraduceAConflictoReintentable()
    {
        await using var db = new ReconstruccionDbContext(Opciones);
        await using var transaccion =
            await db.Database.BeginTransactionAsync();

        var requestId = Guid.NewGuid();

        // Primera solicitud: registro valido.
        new RegistroSolicitudesEf(db).Registrar(
            new SolicitudProcesada(
                requestId,
                "PruebaIdempotencia",
                new string('A', 64),
                "{\"resultado\":\"primero\"}"));

        await new UnidadDeTrabajoEf(db).ConfirmarAsync();

        // Quitar seguimiento para que el duplicado llegue
        // realmente a PostgreSQL y no falle antes en EF Core.
        db.ChangeTracker.Clear();

        // Segunda solicitud: mismo RequestId.
        new RegistroSolicitudesEf(db).Registrar(
            new SolicitudProcesada(
                requestId,
                "PruebaIdempotencia",
                new string('B', 64),
                "{\"resultado\":\"segundo\"}"));

        var conflicto = await Assert.ThrowsAsync<
            ConflictoDeVersionException>(
                () => new UnidadDeTrabajoEf(db).ConfirmarAsync());

        var errorEf = Assert.IsType<DbUpdateException>(
            conflicto.InnerException);

        var postgres = Assert.IsType<PostgresException>(
            errorEf.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgres.SqlState);

        Assert.Equal(
            "PK_solicitudes_procesadas",
            postgres.ConstraintName);

        // La solicitud original permanece.
        var registros = await db
            .Set<SolicitudProcesadaPersistida>()
            .AsNoTracking()
            .CountAsync(x => x.RequestId == requestId);

        Assert.Equal(1, registros);

        // El ensayo completo se revierte.
        await transaccion.RollbackAsync();
    }
}