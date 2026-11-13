using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;

internal static class PruebaOutbox
{
    public static async Task EjecutarAsync(
        DbContextOptions<ReconstruccionDbContext> opciones,
        NpgsqlTransaction transaccion)
    {
        Console.WriteLine("Iniciando prueba de los 17 eventos del Outbox.");

        var desastre = Guid.NewGuid();
        var agregado = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;

        IEventoDominio[] eventos =
        [
            new NecesidadesRecibidas(
                desastre, agregado, 1, fecha,
                [Guid.NewGuid(), Guid.NewGuid()]),

            new NecesidadDescartada(
                desastre, agregado, 2, fecha, Guid.NewGuid()),

            new NecesidadReactivada(
                desastre, agregado, 3, fecha, Guid.NewGuid()),

            new PlanCompletado(
                desastre, agregado, 4, fecha),

            new PlanReabierto(
                desastre, agregado, 5, fecha),

            new PlanCerrado(
                desastre, agregado, 6, fecha),

            new ProyectoCreado(
                desastre, agregado, 7, fecha, [Guid.NewGuid()]),

            new ParticipantesAsignados(
                desastre, agregado, 8, fecha),

            new ProyectoEstadoCambiado(
                desastre, agregado, 9, fecha,
                default, default, "Motivo de prueba"),

            new AvanceRegistrado(
                desastre, agregado, 10, fecha,
                Guid.NewGuid(), 25m, 45m),

            new CostoRegistrado(
                desastre, agregado, 11, fecha,
                Guid.NewGuid(), false),

            new HitoReprogramado(
                desastre, agregado, 12, fecha, Guid.NewGuid()),

            new DocumentoAdjuntado(
                desastre, agregado, 13, fecha,
                Guid.NewGuid(), default),

            new CambioPresupuestalSolicitado(
                desastre, agregado, 14, fecha, Guid.NewGuid()),

            new CambioPresupuestalResuelto(
                desastre, agregado, 15, fecha,
                Guid.NewGuid(), default),

            new AlertaGenerada(
                desastre, agregado, 16, fecha,
                Guid.NewGuid(), default, Guid.NewGuid(),
                EnPlan: true),

            new AlertaResuelta(
                desastre, agregado, 17, fecha,
                Guid.NewGuid(), default,
                EnPlan: true)
        ];

        if (eventos.Length != 17 ||
            eventos.Select(x => x.EventoId).Distinct().Count() != 17)
        {
            throw new Exception(
                "Se esperaban 17 eventos con identificadores diferentes.");
        }

        // Rechazar tipos de eventos no registrados en el catalogo.
        var tipoDesconocidoRechazado = false;

        try
        {
            SerializadorEventosOutbox.Deserializar(
                "EventoDesconocido", "{}");
        }
        catch (NotSupportedException)
        {
            tipoDesconocidoRechazado = true;
        }

        if (!tipoDesconocidoRechazado)
        {
            throw new Exception(
                "El serializador acepto un evento desconocido.");
        }

        Console.WriteLine("Catalogo de eventos validado.");

        const string punto = "paso65_outbox";

        await transaccion.SaveAsync(punto);

        try
        {
            using var db = new ReconstruccionDbContext(opciones);

            await db.Database.UseTransactionAsync(transaccion);

            var outbox = new OutboxEf(db);

            // Registrar sin ejecutar SaveChanges dentro del adaptador.
            outbox.Agregar(eventos);

            var pendientes = db.ChangeTracker
                .Entries<Reconstruccion.Infraestructura.Persistencia.Registros.EventoOutboxPersistido>()
                .Count(x => x.State == EntityState.Added);

            if (pendientes != 17)
            {
                throw new Exception(
                    "OutboxEf no registro exactamente 17 eventos pendientes.");
            }

            Console.WriteLine(
                "Los 17 eventos estan preparados en el DbContext.");

            // La unidad de trabajo ejecutara este guardado en produccion.
            await db.SaveChangesAsync();

            var ids = eventos.Select(x => x.EventoId).ToArray();

            // AsNoTracking obliga a recuperar filas reales de PostgreSQL.
            var filas = await db.EventosOutbox
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .OrderBy(x => x.Secuencia)
                .ToListAsync();

            if (filas.Count != 17)
            {
                throw new Exception(
                    "PostgreSQL no recupero los 17 eventos.");
            }

            if (filas.Any(x => x.Secuencia <= 0) ||
                filas.Select(x => x.Secuencia).Distinct().Count() != 17)
            {
                throw new Exception(
                    "Las secuencias generadas por PostgreSQL son incorrectas.");
            }

            Console.WriteLine(
                "PostgreSQL guardo 17 eventos con secuencias unicas.");

            // Comprobar la reconstruccion del tipo y contenido de cada evento.
            foreach (var original in eventos)
            {
                var fila = filas.Single(x => x.Id == original.EventoId);

                if (fila.TipoEvento != original.GetType().Name)
                {
                    throw new Exception(
                        "El tipo de evento no coincide.");
                }

                if (fila.DesastreId != original.DesastreId ||
                    fila.AgregadoId != original.AgregadoId ||
                    fila.VersionAgregado != original.VersionAgregado)
                {
                    throw new Exception(
                        "El evento perdio metadatos de Dominio.");
                }

                using var documento = JsonDocument.Parse(
                    fila.ContenidoJson);

                var idJson = documento.RootElement
                    .GetProperty("eventoId")
                    .GetGuid();

                if (idJson != original.EventoId)
                {
                    throw new Exception(
                        "El JSONB no conserva EventoId.");
                }

                var reconstruido =
                    SerializadorEventosOutbox.Deserializar(
                        fila.TipoEvento,
                        fila.ContenidoJson);

                if (reconstruido.GetType() != original.GetType() ||
                    reconstruido.EventoId != original.EventoId ||
                    reconstruido.DesastreId != original.DesastreId ||
                    reconstruido.AgregadoId != original.AgregadoId ||
                    reconstruido.VersionAgregado != original.VersionAgregado ||
                    reconstruido.OcurridoEn != original.OcurridoEn)
                {
                    throw new Exception(
                        $"Error reconstruyendo {fila.TipoEvento}.");
                }
            }

            Console.WriteLine(
                "Los 17 eventos fueron serializados y reconstruidos correctamente.");

            // Verificar propiedades especificas de las alertas.
            var filaGenerada = filas.Single(
                x => x.TipoEvento == nameof(AlertaGenerada));

            var filaResuelta = filas.Single(
                x => x.TipoEvento == nameof(AlertaResuelta));

            var alertaGenerada =
                (AlertaGenerada)SerializadorEventosOutbox.Deserializar(
                    filaGenerada.TipoEvento,
                    filaGenerada.ContenidoJson);

            var alertaResuelta =
                (AlertaResuelta)SerializadorEventosOutbox.Deserializar(
                    filaResuelta.TipoEvento,
                    filaResuelta.ContenidoJson);

            if (!alertaGenerada.EnPlan || !alertaResuelta.EnPlan)
            {
                throw new Exception(
                    "Se perdio el indicador EnPlan.");
            }

            using var jsonGenerada = JsonDocument.Parse(
                filaGenerada.ContenidoJson);

            if (!jsonGenerada.RootElement
                .GetProperty("enPlan").GetBoolean())
            {
                throw new Exception(
                    "EnPlan no se almaceno correctamente en JSONB.");
            }

            // Comprobar otros datos especificos de eventos.
            var filaEstado = filas.Single(
                x => x.TipoEvento == nameof(ProyectoEstadoCambiado));

            var estado =
                (ProyectoEstadoCambiado)
                SerializadorEventosOutbox.Deserializar(
                    filaEstado.TipoEvento,
                    filaEstado.ContenidoJson);

            if (estado.Motivo != "Motivo de prueba")
            {
                throw new Exception(
                    "Se perdio el motivo del cambio de estado.");
            }

            Console.WriteLine(
                "EventoId, VersionAgregado, EnPlan y contenido especifico verificados.");
        }
        finally
        {
            // No confirmar datos de laboratorio.
            await transaccion.RollbackAsync(punto);
            await transaccion.ReleaseAsync(punto);
        }

        // Verificar que el savepoint retiro las 17 filas.
        using (var comprobacion = new ReconstruccionDbContext(opciones))
        {
            await comprobacion.Database
                .UseTransactionAsync(transaccion);

            var ids = eventos.Select(x => x.EventoId).ToArray();

            var restantes = await comprobacion.EventosOutbox
                .CountAsync(x => ids.Contains(x.Id));

            if (restantes != 0)
            {
                throw new Exception(
                    "El rollback del Outbox dejo eventos persistidos.");
            }
        }

        Console.WriteLine(
            "ROLLBACK del Outbox confirmado: cero eventos de prueba.");

        Console.WriteLine("PRUEBA DE OUTBOX SUPERADA.");
    }
}