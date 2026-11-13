using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class RepositoriosPostgreSqlEnContenedorTests
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

        await using var db = new ReconstruccionDbContext(Opciones);

        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() =>
        _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task PlanYProyectos_SeRecuperanConOwnedYFiltradoDeEstados()
    {
        var ahora = DateTimeOffset.UtcNow;
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);

        var desastreId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var proyectoEjecutandoId = Guid.NewGuid();
        var proyectoPropuestoId = Guid.NewGuid();

        var necesidadId = Guid.NewGuid();

        // 1. Crear un Plan.
        var plan = PlanReconstruccion.Abrir(
            planId,
            desastreId);

        // 2. Crear un Proyecto que terminara en ejecucion.
        var ejecutando = ProyectoReconstruccion.Proponer(
            proyectoEjecutandoId,
            desastreId,
            Guid.NewGuid(),
            "Proyecto en ejecucion",
            "Prueba de repositorios con PostgreSQL",
            TipoProyecto.Reconstruccion,
            [necesidadId],
            ahora);

        ejecutando.EditarPresupuesto(
            1000m,
            [
                new Aporte("Nacion", 700m),
                new Aporte("Departamento", 300m)
            ]);

        ejecutando.AsignarParticipantes(
            new Responsable(Guid.NewGuid(), Guid.NewGuid()),
            new Contratista("900123", "Obras SAS"),
            ahora);

        ejecutando.DefinirCronograma(
            hoy.AddDays(-30),
            hoy.AddDays(60),
            [
                (
                    "Cimentacion",
                    hoy.AddDays(-30),
                    hoy.AddDays(10),
                    60m
                ),
                (
                    "Estructura",
                    hoy.AddDays(11),
                    hoy.AddDays(60),
                    40m
                )
            ],
            Guid.NewGuid);

        ejecutando.Aprobar(Guid.NewGuid(), ahora);
        ejecutando.Iniciar(ahora);
        ejecutando.LimpiarEventos();

        Assert.Equal(
            EstadoProyecto.EnEjecucion,
            ejecutando.Estado);

        var versionEsperada = ejecutando.Version;

        // 3. Crear otro Proyecto que NO esta en ejecucion.
        var propuesto = ProyectoReconstruccion.Proponer(
            proyectoPropuestoId,
            desastreId,
            Guid.NewGuid(),
            "Proyecto propuesto",
            "Todavia no debe aparecer como ejecutando",
            TipoProyecto.Reconstruccion,
            [Guid.NewGuid()],
            ahora);

        Assert.NotEqual(
            EstadoProyecto.EnEjecucion,
            propuesto.Estado);

        // 4. Guardar por repositorios, con una unidad de trabajo.
        await using (var escritura =
            new ReconstruccionDbContext(Opciones))
        {
            var repositorioPlanes =
                new RepositorioPlanesEf(escritura);

            var repositorioProyectos =
                new RepositorioProyectosEf(escritura);

            repositorioPlanes.Agregar(plan);
            repositorioProyectos.Agregar(ejecutando);
            repositorioProyectos.Agregar(propuesto);

            await new UnidadDeTrabajoEf(
                escritura).ConfirmarAsync();
        }

        // 5. Recuperar desde un DbContext totalmente nuevo.
        await using var lectura =
            new ReconstruccionDbContext(Opciones);

        var planes = new RepositorioPlanesEf(lectura);
        var proyectos = new RepositorioProyectosEf(lectura);

        var planLeido =
            await planes.ObtenerPorDesastreAsync(desastreId);

        Assert.NotNull(planLeido);

        Assert.Equal(planId, planLeido.Id);
        Assert.Equal(desastreId, planLeido.DesastreId);

        // Comprobar que el repositorio utiliza tracking.
        Assert.Equal(
            EntityState.Unchanged,
            lectura.Entry(planLeido).State);

        var proyectoLeido =
            await proyectos.ObtenerPorIdAsync(
                proyectoEjecutandoId);

        Assert.NotNull(proyectoLeido);

        Assert.Equal(
            proyectoEjecutandoId,
            proyectoLeido.Id);

        Assert.Equal(
            desastreId,
            proyectoLeido.DesastreId);

        Assert.Equal(
            1000m,
            proyectoLeido.Presupuesto);

        Assert.Contains(
            necesidadId,
            proyectoLeido.NecesidadIds);

        Assert.Equal(
            2,
            proyectoLeido.Aportes.Count);

        Assert.Contains(
            proyectoLeido.Aportes,
            a => a.Fuente == "Nacion" &&
                 a.Monto == 700m);

        Assert.Contains(
            proyectoLeido.Aportes,
            a => a.Fuente == "Departamento" &&
                 a.Monto == 300m);

        Assert.NotNull(proyectoLeido.Responsable);
        Assert.NotNull(proyectoLeido.Contratista);

        Assert.Equal(
            "Obras SAS",
            proyectoLeido.Contratista.Nombre);

        Assert.Equal(
            versionEsperada,
            proyectoLeido.Version);

        Assert.Equal(
            EstadoProyecto.EnEjecucion,
            proyectoLeido.Estado);

        Assert.Equal(
            EntityState.Unchanged,
            lectura.Entry(proyectoLeido).State);

        // 6. Verificar el segundo proyecto.
        var otro =
            await proyectos.ObtenerPorIdAsync(
                proyectoPropuestoId);

        Assert.NotNull(otro);

        Assert.NotEqual(
            EstadoProyecto.EnEjecucion,
            otro.Estado);

        // 7. Los identificadores inexistentes devuelven null.
        Assert.Null(
            await planes.ObtenerPorDesastreAsync(
                Guid.NewGuid()));

        Assert.Null(
            await proyectos.ObtenerPorIdAsync(
                Guid.NewGuid()));

        // 8. El listado debe incluir SOLO el proyecto ejecutando.
        var idsEnEjecucion =
            await proyectos.ListarIdsEnEjecucionAsync();

        var idEncontrado = Assert.Single(idsEnEjecucion);

        Assert.Equal(
            proyectoEjecutandoId,
            idEncontrado);

        Assert.DoesNotContain(
            proyectoPropuestoId,
            idsEnEjecucion);

        // 9. Verificar contra una consulta directa a PostgreSQL.
        var idsDirectos = await lectura.Proyectos
            .Where(p => p.Estado == EstadoProyecto.EnEjecucion)
            .Select(p => p.Id)
            .ToListAsync();

        Assert.Equal(
            idsDirectos.OrderBy(x => x),
            idsEnEjecucion.OrderBy(x => x));
    }
}