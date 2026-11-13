using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;

internal static class PruebaConcurrenciaCostos
{
    public static async Task EjecutarAsync(
        DbContextOptions<ReconstruccionDbContext> opciones,
        NpgsqlTransaction transaccion)
    {
        Console.WriteLine(
            "Iniciando prueba de dos costos concurrentes.");

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

        // Crear un Proyecto valido usando las reglas del Dominio.
        var plan = PlanReconstruccion.Abrir(
            planId, desastreId);

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
        proyecto.LimpiarEventos();

        var versionInicial = proyecto.Version;

        if (proyecto.Estado != EstadoProyecto.EnEjecucion)
        {
            throw new Exception(
                "El Proyecto no quedo en EnEjecucion.");
        }

        Console.WriteLine(
            $"Proyecto preparado en EnEjecucion, version {versionInicial}.");

        const string punto = "paso73_costos";

        await transaccion.SaveAsync(punto);

        try
        {
            // Guardar el estado inicial.
            using (var inicial = new ReconstruccionDbContext(opciones))
            {
                await inicial.Database.UseTransactionAsync(transaccion);

                new RepositorioPlanesEf(inicial).Agregar(plan);
                new RepositorioProyectosEf(inicial).Agregar(proyecto);

                await new UnidadDeTrabajoEf(inicial).ConfirmarAsync();
            }

            Console.WriteLine(
                "Proyecto inicial guardado en PostgreSQL.");

            Guid eventoAId;
            Guid eventoBId;

            // Ambos contextos cargan el mismo Proyecto
            // antes de que cualquiera confirme.
            using (var dbA = new ReconstruccionDbContext(opciones))
            using (var dbB = new ReconstruccionDbContext(opciones))
            {
                await dbA.Database.UseTransactionAsync(transaccion);
                await dbB.Database.UseTransactionAsync(transaccion);

                var proyectoA =
                    await new RepositorioProyectosEf(dbA)
                        .ObtenerPorIdAsync(proyectoId);

                var proyectoB =
                    await new RepositorioProyectosEf(dbB)
                        .ObtenerPorIdAsync(proyectoId);

                if (proyectoA is null || proyectoB is null)
                {
                    throw new Exception(
                        "No se recupero el Proyecto en ambos contextos.");
                }

                if (proyectoA.Version != versionInicial ||
                    proyectoB.Version != versionInicial)
                {
                    throw new Exception(
                        "Los contextos no cargaron la misma version.");
                }

                Console.WriteLine(
                    "Contextos A y B cargaron la misma version.");

                // Ambas operaciones registran costos diferentes.
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

                if (proyectoA.Version != versionInicial + 1 ||
                    proyectoB.Version != versionInicial + 1)
                {
                    throw new Exception(
                        "RegistrarCosto no incremento la version.");
                }

                var eventoA = proyectoA.Eventos
                    .OfType<CostoRegistrado>()
                    .Single(x => x.CostoId == costoAId);

                var eventoB = proyectoB.Eventos
                    .OfType<CostoRegistrado>()
                    .Single(x => x.CostoId == costoBId);

                eventoAId = eventoA.EventoId;
                eventoBId = eventoB.EventoId;

                // Registrar elementos adicionales en cada contexto
                // para comprobar que tampoco quedan cambios parciales.
                new RegistroSolicitudesEf(dbA).Registrar(
                    new SolicitudProcesada(
                        requestA,
                        "RegistrarCosto",
                        new string('A', 64),
                        "{\"resultado\":\"A\"}"));

                new RegistroSolicitudesEf(dbB).Registrar(
                    new SolicitudProcesada(
                        requestB,
                        "RegistrarCosto",
                        new string('B', 64),
                        "{\"resultado\":\"B\"}"));

                var fabrica = new FabricaIndependientePrueba(opciones);

                new RegistroAuditoriaEf(dbA, fabrica).Registrar(
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

                new RegistroAuditoriaEf(dbB, fabrica).Registrar(
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

                new OutboxEf(dbA).Agregar(proyectoA.Eventos);
                new OutboxEf(dbB).Agregar(proyectoB.Eventos);

                // Primera confirmacion.
                await new UnidadDeTrabajoEf(dbA).ConfirmarAsync();

                Console.WriteLine(
                    "Contexto A confirmo el costo y sus registros.");

                // Segunda confirmacion usando una version obsoleta.
                var conflictoCapturado = false;

                try
                {
                    await new UnidadDeTrabajoEf(dbB).ConfirmarAsync();
                }
                catch (ConflictoDeVersionException ex)
                    when (ex.InnerException is DbUpdateConcurrencyException)
                {
                    conflictoCapturado = true;
                }

                if (!conflictoCapturado)
                {
                    throw new Exception(
                        "El contexto B no genero el conflicto de version esperado.");
                }

                Console.WriteLine(
                    "Contexto B rechazado por ConflictoDeVersionException.");
            }

            // Recuperar el estado real desde un tercer contexto.
            using (var verificar = new ReconstruccionDbContext(opciones))
            {
                await verificar.Database.UseTransactionAsync(transaccion);

                var recuperado =
                    await new RepositorioProyectosEf(verificar)
                        .ObtenerPorIdAsync(proyectoId);

                if (recuperado is null)
                {
                    throw new Exception(
                        "El Proyecto desaparecio.");
                }

                if (recuperado.Version != versionInicial + 1)
                {
                    throw new Exception(
                        "La version final es incorrecta.");
                }

                if (recuperado.Costos.Count != 1 ||
                    recuperado.Costos[0].Id != costoAId ||
                    recuperado.Costos[0].Monto != 150m)
                {
                    throw new Exception(
                        "Se guardo un costo incorrecto o el costo rechazado.");
                }

                var solicitudA = await verificar.SolicitudesProcesadas
                    .CountAsync(x => x.RequestId == requestA);

                var solicitudB = await verificar.SolicitudesProcesadas
                    .CountAsync(x => x.RequestId == requestB);

                var auditoriaA = await verificar.Auditorias
                    .CountAsync(x => x.RequestId == requestA);

                var auditoriaB = await verificar.Auditorias
                    .CountAsync(x => x.RequestId == requestB);

                var outboxA = await verificar.EventosOutbox
                    .CountAsync(x => x.Id == eventoAId);

                var outboxB = await verificar.EventosOutbox
                    .CountAsync(x => x.Id == eventoBId);

                if (solicitudA != 1 ||
                    solicitudB != 0 ||
                    auditoriaA != 1 ||
                    auditoriaB != 0 ||
                    outboxA != 1 ||
                    outboxB != 0)
                {
                    throw new Exception(
                        "La operacion B dejo cambios parciales.");
                }

                Console.WriteLine(
                    "Costo A conservado y costo B descartado.");

                Console.WriteLine(
                    "Auditoria, solicitud y Outbox de B descartados.");

                Console.WriteLine(
                    "Version final y coleccion de costos verificadas.");
            }

            Console.WriteLine(
                "PRUEBA DE CONCURRENCIA DE COSTOS SUPERADA.");
        }
        finally
        {
            // Deshacer exclusivamente los registros de este ensayo.
            await transaccion.RollbackAsync(punto);
            await transaccion.ReleaseAsync(punto);
        }

        // Confirmar que el ensayo fue completamente revertido.
        using (var verificar = new ReconstruccionDbContext(opciones))
        {
            await verificar.Database.UseTransactionAsync(transaccion);

            var planes = await verificar.Planes
                .CountAsync(x => x.Id == planId);

            var proyectos = await verificar.Proyectos
                .CountAsync(x => x.Id == proyectoId);

            var solicitudes = await verificar.SolicitudesProcesadas
                .CountAsync(x =>
                    x.RequestId == requestA ||
                    x.RequestId == requestB);

            var auditorias = await verificar.Auditorias
                .CountAsync(x =>
                    x.RequestId == requestA ||
                    x.RequestId == requestB);

            if (planes != 0 ||
                proyectos != 0 ||
                solicitudes != 0 ||
                auditorias != 0)
            {
                throw new Exception(
                    "El rollback final dejo registros de la prueba.");
            }
        }

        Console.WriteLine(
            "ROLLBACK DE COSTOS SUPERADO: ensayo descartado.");
    }
}