using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Registros;

internal static class PruebaRegistroDependencias
{
    public static void Ejecutar(string cadenaConexion)
    {
        Console.WriteLine(
            "Iniciando prueba de inyeccion de dependencias.");

        var servicios = new ServiceCollection();

        servicios.AgregarPersistenciaReconstruccion(cadenaConexion);

        using var proveedor = servicios.BuildServiceProvider(
            new ServiceProviderOptions
            {
                ValidateScopes = true,
                ValidateOnBuild = true
            });

        using var alcance = proveedor.CreateScope();

        var contenedor = alcance.ServiceProvider;

        var db = contenedor.GetRequiredService<
            ReconstruccionDbContext>();

        var dbRepetido = contenedor.GetRequiredService<
            ReconstruccionDbContext>();

        if (!ReferenceEquals(db, dbRepetido))
        {
            throw new Exception(
                "El DbContext no se comparte dentro del mismo alcance.");
        }

        var repositorioPlanes = contenedor.GetRequiredService<
            IRepositorioPlanes>();

        _ = contenedor.GetRequiredService<
            IRepositorioProyectos>();

        var auditoria = contenedor.GetRequiredService<
            IRegistroAuditoria>();

        var solicitudes = contenedor.GetRequiredService<
            IRegistroSolicitudes>();

        var outbox = contenedor.GetRequiredService<
            IOutbox>();

        _ = contenedor.GetRequiredService<
            IUnidadDeTrabajo>();

        Console.WriteLine(
            "Los seis contratos de Persistencia se resolvieron.");

        var desastre = Guid.NewGuid();
        var plan = PlanReconstruccion.Abrir(
            Guid.NewGuid(),
            desastre);

        var request = Guid.NewGuid();

        repositorioPlanes.Agregar(plan);

        auditoria.Registrar(
            new EntradaAuditoria(
                Guid.NewGuid(),
                default(Rol),
                "PruebaDI",
                "Plan",
                plan.Id,
                desastre,
                DateTimeOffset.UtcNow,
                ResultadoOperacion.Exitoso,
                request,
                "Verificacion del contexto compartido"));

        solicitudes.Registrar(
            new SolicitudProcesada(
                request,
                "PruebaDI",
                new string('D', 64),
                "{\"prueba\":\"di\"}"));

        outbox.Agregar(
        [
            new PlanCompletado(
                desastre,
                plan.Id,
                plan.Version,
                DateTimeOffset.UtcNow)
        ]);

        // Sin ejecutar SaveChanges: no se modifica PostgreSQL.
        var planPendiente = db.ChangeTracker
            .Entries<PlanReconstruccion>()
            .Count(x => x.State == EntityState.Added);

        var auditoriaPendiente = db.ChangeTracker
            .Entries<AuditoriaPersistida>()
            .Count(x => x.State == EntityState.Added);

        var solicitudPendiente = db.ChangeTracker
            .Entries<SolicitudProcesadaPersistida>()
            .Count(x => x.State == EntityState.Added);

        var eventoPendiente = db.ChangeTracker
            .Entries<EventoOutboxPersistido>()
            .Count(x => x.State == EntityState.Added);

        if (planPendiente != 1 ||
            auditoriaPendiente != 1 ||
            solicitudPendiente != 1 ||
            eventoPendiente != 1)
        {
            throw new Exception(
                "Los adaptadores no comparten el mismo DbContext.");
        }

        Console.WriteLine(
            "Repositorio, auditoria, solicitud y Outbox comparten el DbContext.");

        // Una fabrica debe entregar una instancia independiente.
        var fabrica = contenedor.GetRequiredService<
            IDbContextFactory<ReconstruccionDbContext>>();

        using var independiente = fabrica.CreateDbContext();

        if (ReferenceEquals(db, independiente))
        {
            throw new Exception(
                "La fabrica reutilizo el DbContext principal.");
        }

        Console.WriteLine(
            "La fabrica crea un DbContext independiente.");

        // Otro alcance no puede compartir el DbContext anterior.
        using var otroAlcance = proveedor.CreateScope();

        var dbOtro = otroAlcance.ServiceProvider
            .GetRequiredService<ReconstruccionDbContext>();

        if (ReferenceEquals(db, dbOtro))
        {
            throw new Exception(
                "Dos alcances compartieron indebidamente el DbContext.");
        }

        Console.WriteLine(
            "Los alcances independientes usan contextos diferentes.");

        Console.WriteLine(
            "PRUEBA DE INYECCION DE DEPENDENCIAS SUPERADA.");
    }
}