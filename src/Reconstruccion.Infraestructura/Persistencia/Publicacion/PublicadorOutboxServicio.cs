using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Reconstruccion.Infraestructura.Persistencia.Publicacion;

/// <summary>
/// Configuracion de ejecucion del publicador.
/// El Host proporciona la identidad Sistema antes de resolver
/// los servicios de Aplicacion.
/// </summary>
internal sealed record ConfiguracionPublicadorOutbox(
    TimeSpan Intervalo,
    int MaxEventosPorDesastre,
    Action<IServiceProvider> PrepararIdentidadSistema);

/// <summary>
/// Ejecuta periodicamente el procesador del Outbox.
///
/// El bloqueo consultivo de PostgreSQL impide que dos instancias
/// de este servicio publiquen simultaneamente desde la misma BD.
/// </summary>
internal sealed class PublicadorOutboxServicio(
    IServiceScopeFactory fabricaScopes,
    ConfiguracionPublicadorOutbox configuracion,
    ILogger<PublicadorOutboxServicio> logger) : BackgroundService
{
    // Llave fija compartida por las instancias del mismo servicio.
    private const long LlavePublicador = 7241160402026L;

    protected override async Task ExecuteAsync(
        CancellationToken cancelacion)
    {
        try
        {
            // Primer intento inmediatamente al iniciar.
            await EjecutarVueltaSeguraAsync(cancelacion);

            using var temporizador =
                new PeriodicTimer(configuracion.Intervalo);

            while (await temporizador.WaitForNextTickAsync(cancelacion))
            {
                await EjecutarVueltaSeguraAsync(cancelacion);
            }
        }
        catch (OperationCanceledException)
            when (cancelacion.IsCancellationRequested)
        {
            // Detencion normal del Host.
        }
    }

    private async Task EjecutarVueltaSeguraAsync(
        CancellationToken cancelacion)
    {
        try
        {
            using var scope = fabricaScopes.CreateScope();

            // IMPORTANTE: antes de resolver DistribuidorEventos.
            configuracion.PrepararIdentidadSistema(
                scope.ServiceProvider);

            var db = scope.ServiceProvider
                .GetRequiredService<ReconstruccionDbContext>();

            // Mantener esta conexion abierta durante el bloqueo.
            await db.Database.OpenConnectionAsync(cancelacion);

            try
            {
                if (!await AdquirirBloqueoAsync(db, cancelacion))
                {
                    logger.LogDebug(
                        "Otra instancia esta publicando el Outbox.");

                    return;
                }

                try
                {
                    var procesador = scope.ServiceProvider
                        .GetRequiredService<ProcesadorLoteOutbox>();

                    var resultado = await procesador.ProcesarAsync(
                        configuracion.MaxEventosPorDesastre,
                        cancelacion);

                    if (resultado.EventosPublicados > 0 ||
                        resultado.EventosFallidos > 0)
                    {
                        logger.LogInformation(
                            "Outbox: {Publicados} publicados, {Fallidos} fallidos, {Desastres} desastres revisados.",
                            resultado.EventosPublicados,
                            resultado.EventosFallidos,
                            resultado.DesastresRevisados);
                    }
                }
                finally
                {
                    await LiberarBloqueoAsync(db);
                }
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
        catch (OperationCanceledException)
            when (cancelacion.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // El Host permanece vivo; la siguiente vuelta
            // vuelve a intentar procesar los eventos.
            logger.LogError(
                ex,
                "Fallo una vuelta de publicacion del Outbox.");
        }
    }

    private static async Task<bool> AdquirirBloqueoAsync(
        ReconstruccionDbContext db,
        CancellationToken cancelacion)
    {
        await using var comando =
            db.Database.GetDbConnection().CreateCommand();

        comando.CommandText =
            $"SELECT pg_try_advisory_lock({LlavePublicador})";

        var resultado =
            await comando.ExecuteScalarAsync(cancelacion);

        return resultado is true;
    }

    private async Task LiberarBloqueoAsync(
        ReconstruccionDbContext db)
    {
        await using var comando =
            db.Database.GetDbConnection().CreateCommand();

        comando.CommandText =
            $"SELECT pg_advisory_unlock({LlavePublicador})";

        var resultado =
            await comando.ExecuteScalarAsync(CancellationToken.None);

        if (resultado is not true)
        {
            logger.LogWarning(
                "PostgreSQL no confirmo la liberacion del bloqueo Outbox.");
        }
    }
}

/// <summary>
/// Registro independiente del Host.
/// La identidad Sistema se proporciona mediante un callback.
/// </summary>
public static class RegistroPublicadorOutbox
{
    public static IServiceCollection AgregarPublicadorOutbox(
        this IServiceCollection servicios,
        Action<IServiceProvider> prepararIdentidadSistema,
        TimeSpan? intervalo = null,
        int maxEventosPorDesastre = 25)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(prepararIdentidadSistema);

        var periodo = intervalo ?? TimeSpan.FromSeconds(5);

        if (periodo < TimeSpan.FromMilliseconds(500))
        {
            throw new ArgumentOutOfRangeException(
                nameof(intervalo),
                "El intervalo debe ser de al menos 500 ms.");
        }

        if (maxEventosPorDesastre <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEventosPorDesastre));
        }

        if (servicios.Any(x =>
            x.ServiceType == typeof(ConfiguracionPublicadorOutbox)))
        {
            throw new InvalidOperationException(
                "El publicador ya esta registrado.");
        }

        servicios.AddSingleton(
            new ConfiguracionPublicadorOutbox(
                periodo,
                maxEventosPorDesastre,
                prepararIdentidadSistema));

        servicios.AddScoped<
            IEntregaEventoOutbox,
            EntregaEventoOutbox>();

        servicios.AddScoped<ProcesadorLoteOutbox>();

        servicios.AddHostedService<PublicadorOutboxServicio>();

        return servicios;
    }
}