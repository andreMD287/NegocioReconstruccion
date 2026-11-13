using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class ConcurrenciaParticipantesPostgreSqlEnContenedorTests
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
        await _postgres.StartAsync();

        await using var db =
            new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() =>
        _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task DosContextos_CambioDeParticipantesObsoletoNoSobrescribeElPrimero()
    {
        var ahora = DateTimeOffset.UtcNow;

        var desastreId = Guid.NewGuid();
        var proyectoId = Guid.NewGuid();
        var necesidadId = Guid.NewGuid();

        const string contratistaOriginal =
            "Contratista de prueba";

        // 1. Crear el proyecto inicial.
        var proyecto = ProyectoReconstruccion.Proponer(
            proyectoId,
            desastreId,
            Guid.NewGuid(),
            "Proyecto concurrencia participantes",
            "Verificacion de version optimista",
            default,
            [necesidadId],
            ahora);

        proyecto.EditarPresupuesto(
            75000m,
            [new Aporte("Fondo de prueba", 75000m)]);

        proyecto.AsignarParticipantes(
            new Responsable(
                Guid.NewGuid(),
                Guid.NewGuid()),
            new Contratista(
                "PRUEBA-001",
                contratistaOriginal),
            ahora);

        await using (var inicial =
            new ReconstruccionDbContext(Opciones))
        {
            new RepositorioProyectosEf(inicial)
                .Agregar(proyecto);

            await new UnidadDeTrabajoEf(inicial)
                .ConfirmarAsync();
        }

        // 2. Dos DbContext independientes cargan
        // la misma version del Proyecto.
        await using var contextoA =
            new ReconstruccionDbContext(Opciones);

        await using var contextoB =
            new ReconstruccionDbContext(Opciones);

        var proyectoA = await contextoA.Proyectos
            .SingleAsync(p => p.Id == proyectoId);

        var proyectoB = await contextoB.Proyectos
            .SingleAsync(p => p.Id == proyectoId);

        var versionOriginal = proyectoA.Version;

        Assert.Equal(
            versionOriginal,
            proyectoB.Version);

        Assert.Equal(
            contratistaOriginal,
            proyectoA.Contratista?.Nombre);

        Assert.Equal(
            contratistaOriginal,
            proyectoB.Contratista?.Nombre);

        // 3. Contexto A actualiza el responsable.
        var nuevoUsuarioId = Guid.NewGuid();

        proyectoA.AsignarParticipantes(
            new Responsable(
                nuevoUsuarioId,
                Guid.NewGuid()),
            null,
            DateTimeOffset.UtcNow);

        await contextoA.SaveChangesAsync();

        Assert.Equal(
            versionOriginal + 1,
            proyectoA.Version);

        // 4. Contexto B aun conserva la version anterior.
        // Intenta modificar el contratista.
        proyectoB.AsignarParticipantes(
            null,
            new Contratista(
                "PRUEBA-B",
                "Contratista B"),
            DateTimeOffset.UtcNow);

        Assert.Equal(
            versionOriginal + 1,
            proyectoB.Version);

        // 5. PostgreSQL y EF Core deben impedir
        // que el contexto obsoleto sobrescriba el cambio.
        var excepcion = await Assert.ThrowsAsync<
            DbUpdateConcurrencyException>(
                () => contextoB.SaveChangesAsync());

        Assert.NotEmpty(excepcion.Entries);

        // 6. Comprobar el estado realmente persistido.
        await using var verificacion =
            new ReconstruccionDbContext(Opciones);

        var proyectoFinal = await verificacion.Proyectos
            .AsNoTracking()
            .SingleAsync(p => p.Id == proyectoId);

        // La version solo aumento una vez.
        Assert.Equal(
            versionOriginal + 1,
            proyectoFinal.Version);

        // Se conserva el responsable actualizado por A.
        Assert.NotNull(proyectoFinal.Responsable);

        Assert.Equal(
            nuevoUsuarioId,
            proyectoFinal.Responsable.UsuarioId);

        // El intento de B NO reemplazo al contratista.
        Assert.NotNull(proyectoFinal.Contratista);

        Assert.Equal(
            contratistaOriginal,
            proyectoFinal.Contratista.Nombre);

        Assert.NotEqual(
            "Contratista B",
            proyectoFinal.Contratista.Nombre);
    }
}