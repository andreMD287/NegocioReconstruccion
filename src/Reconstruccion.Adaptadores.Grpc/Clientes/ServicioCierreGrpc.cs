using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Contratos.V1;

namespace Reconstruccion.Adaptadores.Grpc.Clientes;

/// <summary>
/// Opciones compartidas del cliente gRPC hacia Cierre.
/// </summary>
internal sealed record ConfiguracionClienteCierreGrpc(
    string Direccion,
    TimeSpan Timeout,
    int MaxIntentos,
    int UmbralCircuito,
    TimeSpan DuracionApertura);

/// <summary>
/// Circuit breaker compartido por todas las solicitudes.
/// La prueba de recuperacion es exclusiva.
/// </summary>
internal sealed class CircuitoCierreGrpc
{
    private readonly object _sincronizacion = new();

    private int _fallosConsecutivos;
    private DateTimeOffset? _abiertoHasta;
    private bool _pruebaEnCurso;

    public bool IntentarEntrar(out bool pruebaRecuperacion)
    {
        lock (_sincronizacion)
        {
            pruebaRecuperacion = false;

            if (_abiertoHasta is null)
            {
                return true;
            }

            if (DateTimeOffset.UtcNow < _abiertoHasta.Value ||
                _pruebaEnCurso)
            {
                return false;
            }

            _pruebaEnCurso = true;
            pruebaRecuperacion = true;

            return true;
        }
    }

    public void RegistrarExito()
    {
        lock (_sincronizacion)
        {
            _fallosConsecutivos = 0;
            _abiertoHasta = null;
            _pruebaEnCurso = false;
        }
    }

    public void RegistrarFallo(
        int umbral,
        TimeSpan duracionApertura)
    {
        lock (_sincronizacion)
        {
            _fallosConsecutivos++;

            if (_abiertoHasta is not null ||
                _fallosConsecutivos >= umbral)
            {
                _fallosConsecutivos = umbral;
                _abiertoHasta =
                    DateTimeOffset.UtcNow.Add(duracionApertura);
            }

            _pruebaEnCurso = false;
        }
    }

    public void LiberarPruebaCancelada(bool eraPrueba)
    {
        if (!eraPrueba)
        {
            return;
        }

        lock (_sincronizacion)
        {
            _pruebaEnCurso = false;
        }
    }
}

/// <summary>
/// Adaptador del puerto IServicioCierre mediante gRPC.
///
/// Se reintentan exclusivamente errores temporales.
/// Cierre debe registrar las notificaciones idempotentemente
/// por desastre y version.
/// </summary>
internal sealed class ServicioCierreGrpc(
    CierreService.CierreServiceClient cliente,
    CircuitoCierreGrpc circuito,
    ConfiguracionClienteCierreGrpc opciones) : IServicioCierre
{
    public Task<RespuestaCierre> NotificarPlanCompletadoAsync(
        Guid desastreId,
        long versionPlan,
        CancellationToken cancelacion = default)
    {
        ValidarIdentificadores(desastreId, versionPlan);

        var solicitud = new NotificarPlanCompletadoRequest
        {
            DesastreId = desastreId.ToString("D"),
            VersionPlan = versionPlan
        };

        return EjecutarAsync(
            (plazo, token) =>
                cliente.NotificarPlanCompletadoAsync(
                    solicitud,
                    deadline: plazo,
                    cancellationToken: token).ResponseAsync,
            cancelacion);
    }

    public Task<RespuestaCierre> NotificarReaperturaAsync(
        Guid desastreId,
        long versionPlan,
        CancellationToken cancelacion = default)
    {
        ValidarIdentificadores(desastreId, versionPlan);

        var solicitud = new NotificarReaperturaRequest
        {
            DesastreId = desastreId.ToString("D"),
            VersionPlan = versionPlan
        };

        return EjecutarAsync(
            (plazo, token) =>
                cliente.NotificarReaperturaAsync(
                    solicitud,
                    deadline: plazo,
                    cancellationToken: token).ResponseAsync,
            cancelacion);
    }

    private async Task<RespuestaCierre> EjecutarAsync(
        Func<DateTime, CancellationToken, Task<RespuestaCierreGrpc>> llamar,
        CancellationToken cancelacion)
    {
        cancelacion.ThrowIfCancellationRequested();

        if (!circuito.IntentarEntrar(out var pruebaRecuperacion))
        {
            throw new RpcException(
                new Status(
                    StatusCode.Unavailable,
                    "Circuit breaker de Cierre abierto."));
        }

        for (var intento = 1; intento <= opciones.MaxIntentos; intento++)
        {
            try
            {
                cancelacion.ThrowIfCancellationRequested();

                var plazo = DateTime.UtcNow.Add(opciones.Timeout);

                var respuesta = await llamar(
                    plazo,
                    cancelacion);

                // La comunicacion funciono, aunque Cierre
                // pueda rechazar la operacion de negocio.
                circuito.RegistrarExito();

                return new RespuestaCierre(
                    respuesta.Aceptada,
                    string.IsNullOrWhiteSpace(respuesta.Motivo)
                        ? null
                        : respuesta.Motivo);
            }
            catch (OperationCanceledException)
                when (cancelacion.IsCancellationRequested)
            {
                circuito.LiberarPruebaCancelada(
                    pruebaRecuperacion);

                throw;
            }
            catch (RpcException ex)
                when (EsFalloTemporal(ex))
            {
                if (intento == opciones.MaxIntentos)
                {
                    circuito.RegistrarFallo(
                        opciones.UmbralCircuito,
                        opciones.DuracionApertura);

                    throw;
                }

                try
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(150 * intento),
                        cancelacion);
                }
                catch (OperationCanceledException)
                    when (cancelacion.IsCancellationRequested)
                {
                    circuito.LiberarPruebaCancelada(
                        pruebaRecuperacion);

                    throw;
                }
            }
            catch (RpcException)
            {
                // Una respuesta gRPC no temporal no indica
                // indisponibilidad: no debe abrir el circuito.
                circuito.RegistrarExito();
                throw;
            }
            catch (Exception)
            {
                circuito.RegistrarFallo(
                    opciones.UmbralCircuito,
                    opciones.DuracionApertura);

                throw;
            }
        }

        throw new InvalidOperationException(
            "Se agotaron los intentos de comunicacion con Cierre.");
    }

    private static bool EsFalloTemporal(RpcException ex) =>
        ex.StatusCode is
            StatusCode.Unavailable or
            StatusCode.DeadlineExceeded or
            StatusCode.ResourceExhausted;

    private static void ValidarIdentificadores(
        Guid desastreId,
        long versionPlan)
    {
        if (desastreId == Guid.Empty)
        {
            throw new ArgumentException(
                "El desastre no puede estar vacio.",
                nameof(desastreId));
        }

        if (versionPlan < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(versionPlan));
        }
    }
}

/// <summary>
/// Registro del cliente real. El Host debe invocar esta extension
/// con la direccion de Cierre tomada de configuracion.
/// </summary>
public static class RegistroClienteCierreGrpc
{
    public static IServiceCollection AgregarClienteCierreGrpc(
        this IServiceCollection servicios,
        string direccionCierre,
        TimeSpan? timeout = null,
        int maxIntentos = 3,
        int umbralCircuito = 3,
        TimeSpan? duracionApertura = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentException.ThrowIfNullOrWhiteSpace(direccionCierre);

        if (!Uri.TryCreate(
                direccionCierre,
                UriKind.Absolute,
                out var direccion) ||
            direccion.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException(
                "Cierre requiere una URL absoluta HTTP o HTTPS.",
                nameof(direccionCierre));
        }

        var tiempoEspera = timeout ?? TimeSpan.FromSeconds(3);
        var apertura = duracionApertura ?? TimeSpan.FromSeconds(15);

        if (tiempoEspera <= TimeSpan.Zero ||
            apertura <= TimeSpan.Zero ||
            maxIntentos <= 0 ||
            umbralCircuito <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "La configuracion de resiliencia debe ser positiva.");
        }

        if (servicios.Any(x =>
            x.ServiceType == typeof(ConfiguracionClienteCierreGrpc)))
        {
            throw new InvalidOperationException(
                "El cliente gRPC de Cierre ya fue configurado.");
        }

        servicios.AddSingleton(
            new ConfiguracionClienteCierreGrpc(
                direccion.AbsoluteUri,
                tiempoEspera,
                maxIntentos,
                umbralCircuito,
                apertura));

        // El canal se reutiliza; no crear uno por cada evento.
        servicios.AddSingleton(
            _ => GrpcChannel.ForAddress(direccion));

        servicios.AddSingleton(sp =>
            new CierreService.CierreServiceClient(
                sp.GetRequiredService<GrpcChannel>()));

        servicios.AddSingleton<CircuitoCierreGrpc>();

        // El Host tiene un fallback TryAddScoped.
        // Nuestro registro concreto debe resolver IServicioCierre.
        servicios.AddScoped<IServicioCierre, ServicioCierreGrpc>();

        return servicios;
    }
}