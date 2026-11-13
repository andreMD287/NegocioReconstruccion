using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Registros;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class RegistroDependenciasPersistenciaTests
{
    [Fact]
    public void SeisContratos_CompartenContextoPorAlcanceYSonIndependientesEntreAlcances()
    {
        // No se conecta a PostgreSQL: esta prueba verifica
        // exclusivamente el registro de DI y el ChangeTracker.
        const string conexionNoUtilizada =
            "Host=127.0.0.1;Port=1;" +
            "Database=prueba_di;" +
            "Username=prueba;Password=prueba;" +
            "Timeout=1";

        using var proveedor = new ServiceCollection()
            .AgregarPersistenciaReconstruccion(conexionNoUtilizada)
            .BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true,
                    ValidateOnBuild = true
                });

        using var alcance = proveedor.CreateScope();

        var servicios = alcance.ServiceProvider;

        var db = servicios.GetRequiredService<
            ReconstruccionDbContext>();

        var dbRepetido = servicios.GetRequiredService<
            ReconstruccionDbContext>();

        // Un mismo alcance siempre recibe el mismo DbContext.
        Assert.Same(db, dbRepetido);

        // Resolver los seis contratos de Persistencia.
        var repositorioPlanes = servicios.GetRequiredService<
            IRepositorioPlanes>();

        Assert.NotNull(
            servicios.GetRequiredService<IRepositorioProyectos>());

        var auditoria = servicios.GetRequiredService<
            IRegistroAuditoria>();

        var solicitudes = servicios.GetRequiredService<
            IRegistroSolicitudes>();

        var outbox = servicios.GetRequiredService<
            IOutbox>();

        Assert.NotNull(
            servicios.GetRequiredService<IUnidadDeTrabajo>());

        var desastreId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var requestId = Guid.NewGuid();

        var plan = PlanReconstruccion.Abrir(
            planId,
            desastreId);

        repositorioPlanes.Agregar(plan);

        auditoria.Registrar(
            new EntradaAuditoria(
                Guid.NewGuid(),
                default(Rol),
                "PruebaDI",
                "Plan",
                planId,
                desastreId,
                DateTimeOffset.UtcNow,
                ResultadoOperacion.Exitoso,
                requestId,
                "Seguimiento del contexto compartido"));

        solicitudes.Registrar(
            new SolicitudProcesada(
                requestId,
                "PruebaDI",
                new string('D', 64),
                "{\"prueba\":\"di\"}"));

        // Evento sintetico exclusivo de la prueba de DI.
        outbox.Agregar(
        [
            new PlanCompletado(
                desastreId,
                planId,
                plan.Version,
                DateTimeOffset.UtcNow)
        ]);

        // Los cuatro tipos deben estar pendientes
        // dentro del MISMO DbContext.
        Assert.Equal(
            1,
            db.ChangeTracker
                .Entries<PlanReconstruccion>()
                .Count(x => x.State == EntityState.Added));

        Assert.Equal(
            1,
            db.ChangeTracker
                .Entries<AuditoriaPersistida>()
                .Count(x => x.State == EntityState.Added));

        Assert.Equal(
            1,
            db.ChangeTracker
                .Entries<SolicitudProcesadaPersistida>()
                .Count(x => x.State == EntityState.Added));

        Assert.Equal(
            1,
            db.ChangeTracker
                .Entries<EventoOutboxPersistido>()
                .Count(x => x.State == EntityState.Added));

        // La fabrica debe crear un contexto independiente.
        var fabrica = servicios.GetRequiredService<
            IDbContextFactory<ReconstruccionDbContext>>();

        using var independiente = fabrica.CreateDbContext();

        Assert.NotSame(db, independiente);

        // Un nuevo alcance NO puede reutilizar el DbContext anterior.
        using var segundoAlcance = proveedor.CreateScope();

        var dbSegundoAlcance = segundoAlcance.ServiceProvider
            .GetRequiredService<ReconstruccionDbContext>();

        Assert.NotSame(db, dbSegundoAlcance);

        // No se llama a SaveChanges: los registros
        // quedan unicamente en memoria.
    }
}