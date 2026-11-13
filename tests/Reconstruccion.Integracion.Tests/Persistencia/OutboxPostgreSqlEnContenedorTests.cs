using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Registros;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class OutboxPostgreSqlEnContenedorTests : IAsyncLifetime
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
    public async Task DiecisieteEventos_SeReconstruyenYOrdenanPorSecuencia()
    {
        var desastre = Guid.NewGuid();
        var agregado = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;

        // Mismo catalogo de 17 eventos del Smoke original.
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
                desastre, agregado, 7, fecha,
                [Guid.NewGuid()]),

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
                desastre, agregado, 12, fecha,
                Guid.NewGuid()),

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

        Assert.Equal(17, eventos.Length);

        Assert.Equal(
            17,
            eventos.Select(x => x.EventoId).Distinct().Count());

        Assert.Equal(
            17,
            eventos.Select(x => x.GetType()).Distinct().Count());

        // Seguridad: un tipo desconocido no se puede reconstruir.
        Assert.Throws<NotSupportedException>(
            () => SerializadorEventosOutbox.Deserializar(
                "EventoDesconocido", "{}"));

        var ids = eventos.Select(x => x.EventoId).ToArray();

        // Confirmar los 17 eventos dentro de una transaccion
        // y revertirla tras comprobar la lectura desde PostgreSQL.
        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            await using var transaccion =
                await db.Database.BeginTransactionAsync();

            new OutboxEf(db).Agregar(eventos);

            var pendientes = db.ChangeTracker
                .Entries<EventoOutboxPersistido>()
                .Count(x => x.State == EntityState.Added);

            Assert.Equal(17, pendientes);

            // La Unidad de Trabajo es quien confirma.
            await new UnidadDeTrabajoEf(db).ConfirmarAsync();

            var filas = await db.EventosOutbox
                .AsNoTracking()
                .Where(x => ids.Contains(x.Id))
                .OrderBy(x => x.Secuencia)
                .ToListAsync();

            Assert.Equal(17, filas.Count);

            Assert.All(
                filas,
                fila => Assert.True(fila.Secuencia > 0));

            Assert.Equal(
                17,
                filas.Select(x => x.Secuencia).Distinct().Count());

            // Tipos, identificadores, versiones y JSONB.
            foreach (var evento in eventos)
            {
                var fila = Assert.Single(
                    filas, x => x.Id == evento.EventoId);

                Assert.Equal(
                    evento.GetType().Name,
                    fila.TipoEvento);

                Assert.Equal(
                    evento.DesastreId,
                    fila.DesastreId);

                Assert.Equal(
                    evento.AgregadoId,
                    fila.AgregadoId);

                Assert.Equal(
                    evento.VersionAgregado,
                    fila.VersionAgregado);

                using var documento = JsonDocument.Parse(
                    fila.ContenidoJson);

                Assert.Equal(
                    evento.EventoId,
                    documento.RootElement
                        .GetProperty("eventoId")
                        .GetGuid());

                var reconstruido =
                    SerializadorEventosOutbox.Deserializar(
                        fila.TipoEvento,
                        fila.ContenidoJson);

                Assert.Equal(
                    evento.GetType(),
                    reconstruido.GetType());

                Assert.Equal(
                    evento.EventoId,
                    reconstruido.EventoId);

                Assert.Equal(
                    evento.DesastreId,
                    reconstruido.DesastreId);

                Assert.Equal(
                    evento.AgregadoId,
                    reconstruido.AgregadoId);

                Assert.Equal(
                    evento.VersionAgregado,
                    reconstruido.VersionAgregado);

                Assert.Equal(
                    evento.OcurridoEn,
                    reconstruido.OcurridoEn);
            }

            // Conservar atributos particulares de las alertas.
            var filaGenerada = Assert.Single(
                filas, x => x.TipoEvento == nameof(AlertaGenerada));

            var filaResuelta = Assert.Single(
                filas, x => x.TipoEvento == nameof(AlertaResuelta));

            var alertaGenerada =
                Assert.IsType<AlertaGenerada>(
                    SerializadorEventosOutbox.Deserializar(
                        filaGenerada.TipoEvento,
                        filaGenerada.ContenidoJson));

            var alertaResuelta =
                Assert.IsType<AlertaResuelta>(
                    SerializadorEventosOutbox.Deserializar(
                        filaResuelta.TipoEvento,
                        filaResuelta.ContenidoJson));

            Assert.True(alertaGenerada.EnPlan);
            Assert.True(alertaResuelta.EnPlan);

            using var jsonAlerta = JsonDocument.Parse(
                filaGenerada.ContenidoJson);

            Assert.True(
                jsonAlerta.RootElement
                    .GetProperty("enPlan")
                    .GetBoolean());

            // Conservar motivo del cambio de estado.
            var filaEstado = Assert.Single(
                filas, x => x.TipoEvento ==
                    nameof(ProyectoEstadoCambiado));

            var estado =
                Assert.IsType<ProyectoEstadoCambiado>(
                    SerializadorEventosOutbox.Deserializar(
                        filaEstado.TipoEvento,
                        filaEstado.ContenidoJson));

            Assert.Equal("Motivo de prueba", estado.Motivo);

            await transaccion.RollbackAsync();
        }

        // El rollback debe haber eliminado las 17 filas.
        await using (var verificar =
            new ReconstruccionDbContext(Opciones))
        {
            Assert.Equal(
                0,
                await verificar.EventosOutbox
                    .CountAsync(x => ids.Contains(x.Id)));
        }

        // SEGUNDA PARTE:
        // Eventos del mismo desastre pero agregados diferentes.
        // La VersionAgregado NO representa el orden global.
        var desastreOrden = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var proyectoId = Guid.NewGuid();

        IEventoDominio[] ejemploOrden =
        [
            new PlanCompletado(
                desastreOrden,
                planId,
                9,
                fecha),

            new ProyectoEstadoCambiado(
                desastreOrden,
                proyectoId,
                2,
                fecha,
                default,
                default,
                "Orden entre agregados"),

            new PlanReabierto(
                desastreOrden,
                planId,
                10,
                fecha)
        ];

        // Confirmaciones sucesivas para fijar un orden conocido.
        foreach (var evento in ejemploOrden)
        {
            await using var escritura =
                new ReconstruccionDbContext(Opciones);

            new OutboxEf(escritura).Agregar([evento]);

            await new UnidadDeTrabajoEf(
                escritura).ConfirmarAsync();
        }

        await using (var lectura =
            new ReconstruccionDbContext(Opciones))
        {
            var ordenados = await lectura.EventosOutbox
                .AsNoTracking()
                .Where(
                    x => x.DesastreId == desastreOrden &&
                         x.PublicadoEn == null)
                .OrderBy(x => x.Secuencia)
                .ToListAsync();

            Assert.Equal(3, ordenados.Count);

            Assert.Equal(
                ejemploOrden.Select(x => x.EventoId).ToArray(),
                ordenados.Select(x => x.Id).ToArray());

            Assert.Equal(
                new long[] { 9, 2, 10 },
                ordenados.Select(
                    x => x.VersionAgregado).ToArray());

            Assert.True(
                ordenados[0].Secuencia <
                ordenados[1].Secuencia);

            Assert.True(
                ordenados[1].Secuencia <
                ordenados[2].Secuencia);

            // Ordenar por VersionAgregado produciria
            // un orden distinto e incorrecto.
            var porVersion = ordenados
                .OrderBy(x => x.VersionAgregado)
                .ToArray();

            Assert.Equal(
                ejemploOrden[1].EventoId,
                porVersion[0].Id);
        }
    }
}