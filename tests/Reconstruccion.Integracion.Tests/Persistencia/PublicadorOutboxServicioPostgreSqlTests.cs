using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Publicacion;
using Testcontainers.PostgreSql;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class PublicadorOutboxServicioPostgreSqlTests
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
    public async Task ServicioPeriodico_PreparaSistemaYPublicaEventoPendiente()
    {
        var evento = await CrearEventoPendienteAsync();

        var seguimiento = new SeguimientoEntrega(bloquear: false);

        using var proveedor = CrearProveedor(seguimiento);

        var trabajador = ObtenerTrabajador(proveedor);

        await trabajador.StartAsync(CancellationToken.None);

        try
        {
            await seguimiento.PrimeraEntrega.Task.WaitAsync(
                TimeSpan.FromSeconds(20));

            await VerificarPublicadoAsync(evento.EventoId);

            Assert.Equal(1, seguimiento.Llamadas);
        }
        finally
        {
            seguimiento.Liberar();
            await trabajador.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DosInstancias_NoPublicanSimultaneamenteElMismoEvento()
    {
        var evento = await CrearEventoPendienteAsync();

        // La primera instancia mantendra una entrega en curso.
        // Esto permite verificar que la segunda no la duplique.
        var seguimiento = new SeguimientoEntrega(bloquear: true);

        using var proveedorA = CrearProveedor(seguimiento);
        using var proveedorB = CrearProveedor(seguimiento);

        var trabajadorA = ObtenerTrabajador(proveedorA);
        var trabajadorB = ObtenerTrabajador(proveedorB);

        var segundoIniciado = false;

        await trabajadorA.StartAsync(CancellationToken.None);

        try
        {
            // La primera instancia ya esta dentro de la entrega,
            // por lo tanto debe conservar el bloqueo PostgreSQL.
            await seguimiento.PrimeraEntrega.Task.WaitAsync(
                TimeSpan.FromSeconds(20));

            Assert.Equal(1, seguimiento.Llamadas);

            await trabajadorB.StartAsync(CancellationToken.None);
            segundoIniciado = true;

            // La segunda instancia tiene oportunidad de ejecutar
            // varias vueltas mientras la primera sigue bloqueada.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(1, seguimiento.Llamadas);

            // Liberamos la entrega de la primera instancia.
            seguimiento.Liberar();

            await VerificarPublicadoAsync(evento.EventoId);

            // Ninguna vuelta posterior debe volver a entregarlo.
            await Task.Delay(TimeSpan.FromSeconds(2));

            Assert.Equal(1, seguimiento.Llamadas);
        }
        finally
        {
            seguimiento.Liberar();

            if (segundoIniciado)
            {
                await trabajadorB.StopAsync(CancellationToken.None);
            }

            await trabajadorA.StopAsync(CancellationToken.None);
        }
    }

    private async Task<PlanCerrado> CrearEventoPendienteAsync()
    {
        var evento = new PlanCerrado(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            DateTimeOffset.UtcNow);

        await using var db = new ReconstruccionDbContext(Opciones);

        new OutboxEf(db).Agregar([evento]);

        await db.SaveChangesAsync();

        return evento;
    }

    private ServiceProvider CrearProveedor(
        SeguimientoEntrega seguimiento)
    {
        var servicios = new ServiceCollection();

        servicios.AddLogging();

        servicios.AddDbContext<ReconstruccionDbContext>(
            opciones => opciones.UseNpgsql(
                _postgres.GetConnectionString()));

        servicios.AddScoped<IdentidadSistemaPrueba>();

        servicios.AgregarPublicadorOutbox(
            prepararIdentidadSistema: sp =>
                sp.GetRequiredService<IdentidadSistemaPrueba>()
                    .ComoSistema(),
            intervalo: TimeSpan.FromMilliseconds(600));

        // Sustituimos la entrega real por un doble de prueba.
        // Conservamos el procesador EF y el servicio reales.
        servicios.AddScoped<IEntregaEventoOutbox>(sp =>
            new EntregaDePrueba(
                sp.GetRequiredService<IdentidadSistemaPrueba>(),
                seguimiento));

        return servicios.BuildServiceProvider(
            validateScopes: true);
    }

    private static IHostedService ObtenerTrabajador(
        ServiceProvider proveedor) =>
        Assert.Single(proveedor.GetServices<IHostedService>());

    private async Task VerificarPublicadoAsync(Guid eventoId)
    {
        var limite = DateTimeOffset.UtcNow.AddSeconds(20);

        while (DateTimeOffset.UtcNow < limite)
        {
            await using var db =
                new ReconstruccionDbContext(Opciones);

            var fila = await db.EventosOutbox
                .AsNoTracking()
                .SingleAsync(x => x.Id == eventoId);

            if (fila.PublicadoEn is not null)
            {
                Assert.Equal(1, fila.Intentos);
                Assert.Null(fila.UltimoError);
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(
            "El servicio no marco el evento como publicado.");
    }

    private sealed class IdentidadSistemaPrueba
    {
        public bool EsSistema { get; private set; }

        public void ComoSistema()
        {
            EsSistema = true;
        }
    }

    private sealed class SeguimientoEntrega(bool bloquear)
    {
        private readonly TaskCompletionSource _liberacion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _llamadas;

        public int Llamadas => Volatile.Read(ref _llamadas);

        public TaskCompletionSource PrimeraEntrega { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ResultadoDistribucion> RegistrarAsync(
            IdentidadSistemaPrueba identidad,
            CancellationToken cancelacion)
        {
            if (!identidad.EsSistema)
            {
                throw new InvalidOperationException(
                    "El publicador no preparo la identidad Sistema.");
            }

            Interlocked.Increment(ref _llamadas);

            PrimeraEntrega.TrySetResult();

            if (bloquear)
            {
                await _liberacion.Task.WaitAsync(cancelacion);
            }

            return ResultadoDistribucion.Ok();
        }

        public void Liberar()
        {
            _liberacion.TrySetResult();
        }
    }

    private sealed class EntregaDePrueba(
        IdentidadSistemaPrueba identidad,
        SeguimientoEntrega seguimiento) : IEntregaEventoOutbox
    {
        public Task<ResultadoDistribucion> EntregarAsync(
            IEventoDominio evento,
            CancellationToken cancelacion = default) =>
            seguimiento.RegistrarAsync(identidad, cancelacion);
    }
}