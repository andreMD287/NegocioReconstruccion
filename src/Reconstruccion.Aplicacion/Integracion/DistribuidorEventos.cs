using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Integracion;

/// <summary>Resultado de entregar un evento del outbox.</summary>
/// <param name="Entregado">Si es true, el publicador puede marcar el evento como publicado; si es false, lo reintenta.</param>
public sealed record ResultadoDistribucion(bool Entregado, string? Motivo = null)
{
    public static ResultadoDistribucion Ok() => new(true);

    public static ResultadoDistribucion Reintentar(string motivo) => new(false, motivo);
}

/// <summary>
/// Entrega un evento ya confirmado (sección 8.2). El publicador del outbox lo invoca por cada
/// evento, en orden por desastre, con un usuario de rol Sistema. La entrega es al menos una vez:
/// si algo falla, devuelve <see cref="ResultadoDistribucion.Reintentar"/> y el evento se reintenta.
/// <list type="bullet">
/// <item>Notifica a los grupos SignalR según <see cref="CatalogoNotificaciones"/>.</item>
/// <item>PlanCompletado y PlanReabierto se envían a Cierre por gRPC. Si Cierre acepta un
/// PlanCompletado, el plan pasa a Cerrado solo si la versión sigue vigente (RN-RE-15).</item>
/// </list>
/// </summary>
public sealed class DistribuidorEventos(
    INotificadorTiempoReal notificador,
    IServicioCierre cierre,
    IManejadorComando<RegistrarAceptacionCierre, ResultadoAceptacionCierre> registrarAceptacion)
{
    public async Task<ResultadoDistribucion> DistribuirAsync(IEventoDominio evento, CancellationToken cancelacion = default)
    {
        var notificacion = CatalogoNotificaciones.Para(evento);
        if (notificacion is not null)
        {
            try
            {
                await notificador.NotificarAsync(notificacion, cancelacion);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return ResultadoDistribucion.Reintentar($"No se pudo notificar en tiempo real: {ex.Message}");
            }
        }

        return evento switch
        {
            PlanCompletado completado => await NotificarPlanCompletadoAsync(completado, cancelacion),
            PlanReabierto reabierto => await NotificarReaperturaAsync(reabierto, cancelacion),
            _ => ResultadoDistribucion.Ok()
        };
    }

    private async Task<ResultadoDistribucion> NotificarPlanCompletadoAsync(PlanCompletado evento, CancellationToken cancelacion)
    {
        RespuestaCierre respuesta;
        try
        {
            respuesta = await cierre.NotificarPlanCompletadoAsync(evento.DesastreId, evento.VersionAgregado, cancelacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cierre no disponible: la notificación queda pendiente en el outbox (sección 5.3).
            return ResultadoDistribucion.Reintentar($"Cierre no disponible: {ex.Message}");
        }

        if (!respuesta.Aceptada)
        {
            return ResultadoDistribucion.Reintentar($"Cierre no aceptó la notificación: {respuesta.Motivo}");
        }

        var registro = await registrarAceptacion.ManejarAsync(
            new RegistrarAceptacionCierre(evento.DesastreId, evento.VersionAgregado), cancelacion);

        // Un conflicto de versión significa que el plan cambió mientras tanto; se reintenta y,
        // si la versión ya está superada, el plan simplemente no pasa a Cerrado (RN-RE-15).
        return registro.EsExitoso
            ? ResultadoDistribucion.Ok()
            : ResultadoDistribucion.Reintentar($"No se registró la aceptación: {registro.Error!.Mensaje}");
    }

    private async Task<ResultadoDistribucion> NotificarReaperturaAsync(PlanReabierto evento, CancellationToken cancelacion)
    {
        try
        {
            var respuesta = await cierre.NotificarReaperturaAsync(evento.DesastreId, evento.VersionAgregado, cancelacion);
            return respuesta.Aceptada
                ? ResultadoDistribucion.Ok()
                : ResultadoDistribucion.Reintentar($"Cierre no aceptó la reapertura: {respuesta.Motivo}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ResultadoDistribucion.Reintentar($"Cierre no disponible: {ex.Message}");
        }
    }
}
