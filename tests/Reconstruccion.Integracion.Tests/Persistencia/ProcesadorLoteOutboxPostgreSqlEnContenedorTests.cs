using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Publicacion;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class ProcesadorLoteOutboxPostgreSqlEnContenedorTests
    : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres =
        new PostgreSqlBuilder("postgres:16.13").Build();

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
    public async Task FalloEnUnDesastre_BloqueaSuOrdenPeroPermiteOtroYReintenta()
    {
        var desastreA = Guid.NewGuid();
        var desastreB = Guid.NewGuid();
        var agregadoA = Guid.NewGuid();
        var agregadoB = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var a1 = new PlanCompletado(
            desastreA, agregadoA, 1, ahora);

        var a2 = new PlanReabierto(
            desastreA, agregadoA, 2, ahora);

        var a3 = new PlanCerrado(
            desastreA, agregadoA, 3, ahora);

        var b1 = new PlanCompletado(
            desastreB, agregadoB, 1, ahora);

        await using (var inicial = new ReconstruccionDbContext(Opciones))
        {
            var outbox = new OutboxEf(inicial);

            outbox.Agregar([a1]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([a2]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([a3]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([b1]);
            await inicial.SaveChangesAsync();

            var ordenA = await inicial.EventosOutbox
                .AsNoTracking()
                .Where(x => x.DesastreId == desastreA)
                .OrderBy(x => x.Secuencia)
                .Select(x => x.Id)
                .ToArrayAsync();

            Assert.Equal(
                [a1.EventoId, a2.EventoId, a3.EventoId],
                ordenA);
        }

        var fallarSegundo = true;

        var entrega = new EntregaSimulada(evento =>
        {
            if (evento.EventoId == a2.EventoId && fallarSegundo)
            {
                return ResultadoDistribucion.Reintentar(
                    "Fallo temporal de Cierre");
            }

            return ResultadoDistribucion.Ok();
        });

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var resultado = await new ProcesadorLoteOutbox(
                db, entrega).ProcesarAsync();

            Assert.Equal(2, resultado.DesastresRevisados);
            Assert.Equal(2, resultado.EventosPublicados);
            Assert.Equal(1, resultado.EventosFallidos);
        }

        // A1 se entrega, A2 falla y A3 queda bloqueado.
        Assert.Equal(
            [a1.EventoId, a2.EventoId],
            entrega.Recibidos
                .Where(x => x.DesastreId == desastreA)
                .Select(x => x.EventoId)
                .ToArray());

        // El fallo de A no bloquea al desastre B.
        Assert.Contains(
            entrega.Recibidos,
            x => x.EventoId == b1.EventoId);

        await using (var verificar = new ReconstruccionDbContext(Opciones))
        {
            var filas = await verificar.EventosOutbox
                .AsNoTracking()
                .ToListAsync();

            var filaA1 = Assert.Single(
                filas, x => x.Id == a1.EventoId);

            var filaA2 = Assert.Single(
                filas, x => x.Id == a2.EventoId);

            var filaA3 = Assert.Single(
                filas, x => x.Id == a3.EventoId);

            var filaB1 = Assert.Single(
                filas, x => x.Id == b1.EventoId);

            Assert.NotNull(filaA1.PublicadoEn);
            Assert.Equal(1, filaA1.Intentos);

            Assert.Null(filaA2.PublicadoEn);
            Assert.Equal(1, filaA2.Intentos);
            Assert.Contains(
                "Fallo temporal", filaA2.UltimoError);

            Assert.Null(filaA3.PublicadoEn);
            Assert.Equal(0, filaA3.Intentos);

            Assert.NotNull(filaB1.PublicadoEn);
            Assert.Equal(1, filaB1.Intentos);
        }

        // Simulamos que Cierre vuelve a estar disponible.
        fallarSegundo = false;
        entrega.Recibidos.Clear();

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var resultado = await new ProcesadorLoteOutbox(
                db, entrega).ProcesarAsync();

            Assert.Equal(1, resultado.DesastresRevisados);
            Assert.Equal(2, resultado.EventosPublicados);
            Assert.Equal(0, resultado.EventosFallidos);
        }

        // No vuelve a entregar A1 ni B1.
        Assert.Equal(
            [a2.EventoId, a3.EventoId],
            entrega.Recibidos
                .Select(x => x.EventoId)
                .ToArray());

        await using (var verificar = new ReconstruccionDbContext(Opciones))
        {
            var filas = await verificar.EventosOutbox
                .AsNoTracking()
                .ToListAsync();

            Assert.All(filas, fila =>
                Assert.NotNull(fila.PublicadoEn));

            var filaA2 = Assert.Single(
                filas, x => x.Id == a2.EventoId);

            Assert.Equal(2, filaA2.Intentos);
            Assert.Null(filaA2.UltimoError);
        }
    }

    [Fact]
    public async Task ExcepcionDeEntrega_RegistraErrorYNoPublicaSiguientes()
    {
        var desastre = Guid.NewGuid();
        var agregado = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var primero = new PlanCompletado(
            desastre, agregado, 1, ahora);

        var segundo = new PlanReabierto(
            desastre, agregado, 2, ahora);

        await using (var inicial = new ReconstruccionDbContext(Opciones))
        {
            var outbox = new OutboxEf(inicial);

            outbox.Agregar([primero]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([segundo]);
            await inicial.SaveChangesAsync();
        }

        var entrega = new EntregaSimulada(evento =>
        {
            if (evento.EventoId == primero.EventoId)
            {
                throw new InvalidOperationException(
                    "Caida simulada del distribuidor");
            }

            return ResultadoDistribucion.Ok();
        });

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var resultado = await new ProcesadorLoteOutbox(
                db, entrega).ProcesarAsync();

            Assert.Equal(0, resultado.EventosPublicados);
            Assert.Equal(1, resultado.EventosFallidos);
        }

        Assert.Equal(
            [primero.EventoId],
            entrega.Recibidos
                .Select(x => x.EventoId)
                .ToArray());

        await using (var verificar = new ReconstruccionDbContext(Opciones))
        {
            var filas = await verificar.EventosOutbox
                .AsNoTracking()
                .ToListAsync();

            var filaPrimero = Assert.Single(
                filas, x => x.Id == primero.EventoId);

            var filaSegundo = Assert.Single(
                filas, x => x.Id == segundo.EventoId);

            Assert.Null(filaPrimero.PublicadoEn);
            Assert.Equal(1, filaPrimero.Intentos);
            Assert.Contains(
                "InvalidOperationException",
                filaPrimero.UltimoError);

            Assert.Null(filaSegundo.PublicadoEn);
            Assert.Equal(0, filaSegundo.Intentos);
        }
    }

    [Fact]
    public async Task LimitePorDesastre_ContinuaEnEjecucionesPosteriores()
    {
        var desastre = Guid.NewGuid();
        var agregado = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        var primero = new PlanCompletado(
            desastre, agregado, 1, ahora);

        var segundo = new PlanReabierto(
            desastre, agregado, 2, ahora);

        var tercero = new PlanCerrado(
            desastre, agregado, 3, ahora);

        await using (var inicial = new ReconstruccionDbContext(Opciones))
        {
            var outbox = new OutboxEf(inicial);

            outbox.Agregar([primero]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([segundo]);
            await inicial.SaveChangesAsync();

            outbox.Agregar([tercero]);
            await inicial.SaveChangesAsync();
        }

        var entrega = new EntregaSimulada(
            _ => ResultadoDistribucion.Ok());

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var resultado = await new ProcesadorLoteOutbox(
                db, entrega).ProcesarAsync(
                    maxEventosPorDesastre: 1);

            Assert.Equal(1, resultado.EventosPublicados);
            Assert.Equal(0, resultado.EventosFallidos);
        }

        Assert.Equal(
            [primero.EventoId],
            entrega.Recibidos
                .Select(x => x.EventoId)
                .ToArray());

        await using (var db = new ReconstruccionDbContext(Opciones))
        {
            var resultado = await new ProcesadorLoteOutbox(
                db, entrega).ProcesarAsync(
                    maxEventosPorDesastre: 2);

            Assert.Equal(2, resultado.EventosPublicados);
            Assert.Equal(0, resultado.EventosFallidos);
        }

        Assert.Equal(
            [primero.EventoId, segundo.EventoId, tercero.EventoId],
            entrega.Recibidos
                .Select(x => x.EventoId)
                .ToArray());

        await using (var verificar = new ReconstruccionDbContext(Opciones))
        {
            var filas = await verificar.EventosOutbox
                .AsNoTracking()
                .ToListAsync();

            Assert.Equal(3, filas.Count);

            Assert.All(filas, fila =>
            {
                Assert.NotNull(fila.PublicadoEn);
                Assert.Equal(1, fila.Intentos);
                Assert.Null(fila.UltimoError);
            });
        }
    }

    private sealed class EntregaSimulada(
        Func<IEventoDominio, ResultadoDistribucion> comportamiento)
        : IEntregaEventoOutbox
    {
        public List<IEventoDominio> Recibidos { get; } = [];

        public Task<ResultadoDistribucion> EntregarAsync(
            IEventoDominio evento,
            CancellationToken cancelacion = default)
        {
            cancelacion.ThrowIfCancellationRequested();

            Recibidos.Add(evento);

            return Task.FromResult(comportamiento(evento));
        }
    }
}