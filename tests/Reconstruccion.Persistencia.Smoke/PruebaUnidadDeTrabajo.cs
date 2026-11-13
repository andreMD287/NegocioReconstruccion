using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;

internal static class PruebaUnidadDeTrabajo
{
    public static async Task EjecutarAsync(
        DbContextOptions<ReconstruccionDbContext> opciones,
        NpgsqlTransaction transaccion)
    {
        Console.WriteLine("Iniciando prueba integral de UnidadDeTrabajo.");

        var desastre = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var requestOriginal = Guid.NewGuid();
        var requestDuplicadoPlan = Guid.NewGuid();

        var plan = PlanReconstruccion.Abrir(planId, desastre);

        var evento = new PlanCompletado(
            desastre,
            planId,
            plan.Version,
            DateTimeOffset.UtcNow);

        // Evento artificial exclusivo de una prueba de persistencia.
        // No representa una transicion real del estado del Plan.

        const string punto = "paso70_unidad_trabajo";

        await transaccion.SaveAsync(punto);

        try
        {
            // CASO 1: confirmar los cuatro componentes conjuntamente.
            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                new RepositorioPlanesEf(db).Agregar(plan);

                new RegistroAuditoriaEf(
                    db,
                    new FabricaIndependientePrueba(opciones)
                ).Registrar(
                    new EntradaAuditoria(
                        Guid.NewGuid(),
                        default(Rol),
                        "PruebaUnidadTrabajo",
                        "Plan",
                        planId,
                        desastre,
                        DateTimeOffset.UtcNow,
                        ResultadoOperacion.Exitoso,
                        requestOriginal,
                        "Confirmacion integral de prueba"));

                new RegistroSolicitudesEf(db).Registrar(
                    new SolicitudProcesada(
                        requestOriginal,
                        "AltaPlanPrueba",
                        new string('A', 64),
                        "{\"estado\":\"confirmado\"}"));

                new OutboxEf(db).Agregar([evento]);

                var pendientes = db.ChangeTracker
                    .Entries()
                    .Count(x => x.State == EntityState.Added);

                if (pendientes < 4)
                {
                    throw new Exception(
                        "No se prepararon los cuatro componentes.");
                }

                await new UnidadDeTrabajoEf(db).ConfirmarAsync();
            }

            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                var cantidadPlan = await db.Planes
                    .CountAsync(x => x.Id == planId);

                var cantidadAuditoria = await db.Auditorias
                    .CountAsync(x => x.RequestId == requestOriginal);

                var cantidadSolicitud = await db.SolicitudesProcesadas
                    .CountAsync(x => x.RequestId == requestOriginal);

                var cantidadEvento = await db.EventosOutbox
                    .CountAsync(x => x.Id == evento.EventoId);

                if (cantidadPlan != 1 ||
                    cantidadAuditoria != 1 ||
                    cantidadSolicitud != 1 ||
                    cantidadEvento != 1)
                {
                    throw new Exception(
                        "La confirmacion conjunta no guardo los cuatro componentes.");
                }
            }

            Console.WriteLine(
                "CONFIRMACION INTEGRAL SUPERADA: Plan, auditoria, solicitud y Outbox.");

            // CASO 2: intentar registrar otro Plan para el mismo desastre.
            var segundoPlan = PlanReconstruccion.Abrir(
                Guid.NewGuid(), desastre);

            var eventoDuplicado = new PlanCompletado(
                desastre,
                segundoPlan.Id,
                segundoPlan.Version,
                DateTimeOffset.UtcNow);

            var conflictoCapturado = false;

            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                new RepositorioPlanesEf(db).Agregar(segundoPlan);

                new RegistroAuditoriaEf(
                    db,
                    new FabricaIndependientePrueba(opciones)
                ).Registrar(
                    new EntradaAuditoria(
                        Guid.NewGuid(),
                        default(Rol),
                        "IntentoPlanDuplicado",
                        "Plan",
                        segundoPlan.Id,
                        desastre,
                        DateTimeOffset.UtcNow,
                        ResultadoOperacion.Exitoso,
                        requestDuplicadoPlan,
                        "Debe revertirse"));

                new RegistroSolicitudesEf(db).Registrar(
                    new SolicitudProcesada(
                        requestDuplicadoPlan,
                        "AltaPlanDuplicado",
                        new string('B', 64),
                        "{\"estado\":\"duplicado\"}"));

                new OutboxEf(db).Agregar([eventoDuplicado]);

                try
                {
                    await new UnidadDeTrabajoEf(db).ConfirmarAsync();
                }
                catch (ConflictoDeVersionException ex)
                {
                    if (ex.InnerException is not DbUpdateException errorEf ||
                        errorEf.InnerException is not PostgresException postgres ||
                        postgres.SqlState != PostgresErrorCodes.UniqueViolation ||
                        postgres.ConstraintName !=
                            "IX_planes_reconstruccion_DesastreId")
                    {
                        throw new Exception(
                            "El conflicto no conserva el indice de Plan esperado.",
                            ex);
                    }

                    conflictoCapturado = true;
                }
            }

            if (!conflictoCapturado)
            {
                throw new Exception(
                    "El Plan duplicado no genero ConflictoDeVersionException.");
            }

            Console.WriteLine(
                "PLAN DUPLICADO SUPERADO: PostgreSQL 23505 traducido a conflicto.");

            // CASO 3: comprobar que el intento fallido no guardo
            // accidentalmente auditoria, solicitud ni evento.
            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                var planes = await db.Planes
                    .CountAsync(x => x.DesastreId == desastre);

                var auditoriasFallidas = await db.Auditorias
                    .CountAsync(x => x.RequestId == requestDuplicadoPlan);

                var solicitudesFallidas = await db.SolicitudesProcesadas
                    .CountAsync(x => x.RequestId == requestDuplicadoPlan);

                var eventosFallidos = await db.EventosOutbox
                    .CountAsync(x => x.Id == eventoDuplicado.EventoId);

                if (planes != 1 ||
                    auditoriasFallidas != 0 ||
                    solicitudesFallidas != 0 ||
                    eventosFallidos != 0)
                {
                    throw new Exception(
                        "El intento fallido dejo cambios parciales.");
                }
            }

            Console.WriteLine(
                "ATOMICIDAD SUPERADA: el fallo no guardo datos parciales.");

            // CASO 4: un RequestId duplicado debe convertirse
            // en conflicto reintentable, conservando el error 23505.
            var duplicadoComoConflicto = false;

            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                new RegistroSolicitudesEf(db).Registrar(
                    new SolicitudProcesada(
                        requestOriginal,
                        "SolicitudDuplicada",
                        new string('C', 64),
                        "{\"estado\":\"no guardar\"}"));

                try
                {
                    await new UnidadDeTrabajoEf(db).ConfirmarAsync();
                }
                catch (ConflictoDeVersionException ex)
                    when (ex.InnerException is DbUpdateException errorEf &&
                          errorEf.InnerException is PostgresException pg &&
                          pg.SqlState == PostgresErrorCodes.UniqueViolation &&
                          pg.ConstraintName == "PK_solicitudes_procesadas")
                {
                    duplicadoComoConflicto = true;
                }
            }

            if (!duplicadoComoConflicto)
            {
                throw new Exception(
                    "RequestId duplicado no tuvo el tratamiento esperado.");
            }

            Console.WriteLine(
                "IDEMPOTENCIA SUPERADA: RequestId duplicado genera conflicto reintentable.");

            // El registro original debe continuar intacto.
            using (var db = new ReconstruccionDbContext(opciones))
            {
                await db.Database.UseTransactionAsync(transaccion);

                var solicitud = await db.SolicitudesProcesadas
                    .AsNoTracking()
                    .SingleAsync(x => x.RequestId == requestOriginal);

                if (solicitud.TipoComando != "AltaPlanPrueba")
                {
                    throw new Exception(
                        "El intento duplicado altero la solicitud original.");
                }
            }

            Console.WriteLine("PRUEBA DE UNIDAD DE TRABAJO SUPERADA.");
        }
        finally
        {
            // Descartar todo lo creado por este ensayo.
            await transaccion.RollbackAsync(punto);
            await transaccion.ReleaseAsync(punto);
        }

        // Comprobar que tampoco quedaron datos de la prueba
        // dentro de la transaccion exterior del laboratorio.
        using (var db = new ReconstruccionDbContext(opciones))
        {
            await db.Database.UseTransactionAsync(transaccion);

            var planes = await db.Planes
                .CountAsync(x => x.DesastreId == desastre);

            var solicitudes = await db.SolicitudesProcesadas
                .CountAsync(x => x.RequestId == requestOriginal ||
                                 x.RequestId == requestDuplicadoPlan);

            var auditorias = await db.Auditorias
                .CountAsync(x => x.RequestId == requestOriginal ||
                                 x.RequestId == requestDuplicadoPlan);

            var eventos = await db.EventosOutbox
                .CountAsync(x => x.Id == evento.EventoId);

            if (planes != 0 || solicitudes != 0 ||
                auditorias != 0 || eventos != 0)
            {
                throw new Exception(
                    "El rollback final dejo datos de prueba.");
            }
        }

        Console.WriteLine(
            "ROLLBACK FINAL SUPERADO: datos de la UnidadDeTrabajo descartados.");
    }
}