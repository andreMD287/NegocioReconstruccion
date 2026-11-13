using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Registros;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class ConcurrenciaCostosPostgreSqlEnContenedorTests
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

    public Task DisposeAsync()
    {
        return _postgres.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task DosContextos_ElSegundoCostoFallaSinDejarRegistrosParciales()
    {
        var ahora = DateTimeOffset.UtcNow;
        var hoy = DateOnly.FromDateTime(ahora.UtcDateTime);

        var desastreId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var proyectoId = Guid.NewGuid();
        var formuladorId = Guid.NewGuid();
        var necesidadId = Guid.NewGuid();

        var costoAId = Guid.NewGuid();
        var costoBId = Guid.NewGuid();

        var requestA = Guid.NewGuid();
        var requestB = Guid.NewGuid();

        // PREPARACION: crear un proyecto que pueda registrar costos.
        var plan = PlanReconstruccion.Abrir(
            planId,
            desastreId);

        var proyecto = ProyectoReconstruccion.Proponer(
            proyectoId,
            desastreId,
            formuladorId,
            "Proyecto de concurrencia",
            "Ensayo de costos sobre una misma version",
            TipoProyecto.Reconstruccion,
            [necesidadId],
            ahora);

        proyecto.EditarPresupuesto(
            1000m,
            [
                new Aporte("Nacion", 700m),
                new Aporte("Departamento", 300m)
            ]);

        proyecto.AsignarParticipantes(
            new Responsable(Guid.NewGuid(), Guid.NewGuid()),
            new Contratista("900123", "Obras SAS"),
            ahora);

        proyecto.DefinirCronograma(
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

        proyecto.Aprobar(Guid.NewGuid(), ahora);
        proyecto.Iniciar(ahora);

        // Los eventos de preparacion no forman parte de esta prueba.
        proyecto.LimpiarEventos();

        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);

        var versionInicial = proyecto.Version;

        // Guardar el Plan y el Proyecto inicial.
        await using (var inicial = new ReconstruccionDbContext(Opciones))
        {
            new RepositorioPlanesEf(inicial).Agregar(plan);
            new RepositorioProyectosEf(inicial).Agregar(proyecto);

            await inicial.SaveChangesAsync();
        }

        // DI real: cada alcance obtiene su propio DbContext.
        // Ambos apuntan al mismo PostgreSQL, pero NO comparten
        // la misma conexion ni una transaccion manual.
        using var proveedor = new ServiceCollection()
            .AgregarPersistenciaReconstruccion(
                _postgres.GetConnectionString())
            .BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        using var alcanceA = proveedor.CreateScope();
        using var alcanceB = proveedor.CreateScope();

        var serviciosA = alcanceA.ServiceProvider;
        var serviciosB = alcanceB.ServiceProvider;

        var dbA = serviciosA.GetRequiredService<
            ReconstruccionDbContext>();

        var dbB = serviciosB.GetRequiredService<
            ReconstruccionDbContext>();

        Assert.NotSame(dbA, dbB);

        // A y B consultan antes de que se confirme ningun costo.
        var proyectoA = await serviciosA.GetRequiredService<
            IRepositorioProyectos>()
            .ObtenerPorIdAsync(proyectoId);

        var proyectoB = await serviciosB.GetRequiredService<
            IRepositorioProyectos>()
            .ObtenerPorIdAsync(proyectoId);

        Assert.NotNull(proyectoA);
        Assert.NotNull(proyectoB);

        Assert.Equal(versionInicial, proyectoA.Version);
        Assert.Equal(versionInicial, proyectoB.Version);

        // Ambos modifican la misma version original.
        proyectoA.RegistrarCosto(
            150m,
            "Costo del contexto A",
            hoy,
            null,
            null,
            costoAId,
            ahora);

        proyectoB.RegistrarCosto(
            200m,
            "Costo del contexto B",
            hoy,
            null,
            null,
            costoBId,
            ahora);

        Assert.Equal(versionInicial + 1, proyectoA.Version);
        Assert.Equal(versionInicial + 1, proyectoB.Version);

        var eventoA = Assert.Single(
            proyectoA.Eventos.OfType<CostoRegistrado>());

        var eventoB = Assert.Single(
            proyectoB.Eventos.OfType<CostoRegistrado>());

        Assert.Equal(costoAId, eventoA.CostoId);
        Assert.Equal(costoBId, eventoB.CostoId);

        // Registrar solicitud, auditoria y Outbox en el contexto A.
        serviciosA.GetRequiredService<
            IRegistroSolicitudes>().Registrar(
                new SolicitudProcesada(
                    requestA,
                    "RegistrarCosto",
                    new string('A', 64),
                    "{\"resultado\":\"A\"}"));

        serviciosA.GetRequiredService<
            IRegistroAuditoria>().Registrar(
                new EntradaAuditoria(
                    Guid.NewGuid(),
                    default(Rol),
                    "RegistrarCosto",
                    "Proyecto",
                    proyectoId,
                    desastreId,
                    ahora,
                    ResultadoOperacion.Exitoso,
                    requestA,
                    "Operacion A"));

        serviciosA.GetRequiredService<
            IOutbox>().Agregar(proyectoA.Eventos);

        // Registrar los mismos tipos de efectos en el contexto B.
        serviciosB.GetRequiredService<
            IRegistroSolicitudes>().Registrar(
                new SolicitudProcesada(
                    requestB,
                    "RegistrarCosto",
                    new string('B', 64),
                    "{\"resultado\":\"B\"}"));

        serviciosB.GetRequiredService<
            IRegistroAuditoria>().Registrar(
                new EntradaAuditoria(
                    Guid.NewGuid(),
                    default(Rol),
                    "RegistrarCosto",
                    "Proyecto",
                    proyectoId,
                    desastreId,
                    ahora,
                    ResultadoOperacion.Exitoso,
                    requestB,
                    "Operacion B"));

        serviciosB.GetRequiredService<
            IOutbox>().Agregar(proyectoB.Eventos);

        // La primera operacion se confirma completamente.
        await serviciosA.GetRequiredService<
            IUnidadDeTrabajo>().ConfirmarAsync();

        // La segunda opera con la version antigua.
        var conflicto = await Assert.ThrowsAsync<
            ConflictoDeVersionException>(
                () => serviciosB.GetRequiredService<
                    IUnidadDeTrabajo>().ConfirmarAsync());

        Assert.IsType<DbUpdateConcurrencyException>(
            conflicto.InnerException);

        // Verificar desde una conexion nueva, fuera de A y B.
        await using var verificar =
            new ReconstruccionDbContext(Opciones);

        var proyectoFinal = await new RepositorioProyectosEf(
            verificar).ObtenerPorIdAsync(proyectoId);

        Assert.NotNull(proyectoFinal);

        Assert.Equal(
            versionInicial + 1,
            proyectoFinal.Version);

        var costoFinal = Assert.Single(proyectoFinal.Costos);

        Assert.Equal(costoAId, costoFinal.Id);
        Assert.Equal(150m, costoFinal.Monto);

        Assert.DoesNotContain(
            proyectoFinal.Costos,
            x => x.Id == costoBId);

        // Solo una solicitud debe existir.
        var solicitudes = await verificar
            .Set<SolicitudProcesadaPersistida>()
            .AsNoTracking()
            .ToListAsync();

        Assert.Single(solicitudes);

        Assert.Equal(
            requestA,
            solicitudes[0].RequestId);

        // Solo la auditoria A debe persistir.
        var auditorias = await verificar
            .Set<AuditoriaPersistida>()
            .AsNoTracking()
            .ToListAsync();

        Assert.Single(auditorias);

        // Solo el evento A debe quedar en el Outbox.
        var eventos = await verificar
            .Set<EventoOutboxPersistido>()
            .AsNoTracking()
            .ToListAsync();

        var eventoPersistido = Assert.Single(eventos);

        Assert.Equal(
            eventoA.EventoId,
            eventoPersistido.Id);

        Assert.DoesNotContain(
            eventos,
            x => x.Id == eventoB.EventoId);

        Assert.Equal(
            desastreId,
            eventoPersistido.DesastreId);
    }
}