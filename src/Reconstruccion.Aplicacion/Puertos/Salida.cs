namespace Reconstruccion.Aplicacion.Puertos;

/// <summary>
/// Respuesta de Cierre. Aceptada significa que Cierre registró la notificación de forma
/// durable, en la respuesta de la misma llamada (sección 7).
/// </summary>
public sealed record RespuestaCierre(bool Aceptada, string? Motivo = null);

/// <summary>
/// Cliente hacia Cierre. Lo implementa el adaptador gRPC, con timeouts, reintentos y
/// circuit breaker; lo invoca el publicador del outbox.
/// </summary>
public interface IServicioCierre
{
    Task<RespuestaCierre> NotificarPlanCompletadoAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default);

    Task<RespuestaCierre> NotificarReaperturaAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default);
}

/// <summary>
/// Notificación mínima en tiempo real: identificador, tipo y versión; el cliente
/// consulta el detalle por gRPC (sección 8.2).
/// </summary>
/// <param name="Grupos">Grupos destino, por ejemplo "desastre-{id}" o "proyecto-{id}".</param>
public sealed record Notificacion(string Tipo, Guid ObjetoId, long Version, IReadOnlyList<string> Grupos);

/// <summary>Canal de notificaciones en tiempo real. Lo implementa el adaptador SignalR.</summary>
public interface INotificadorTiempoReal
{
    Task NotificarAsync(Notificacion notificacion, CancellationToken cancelacion = default);
}
