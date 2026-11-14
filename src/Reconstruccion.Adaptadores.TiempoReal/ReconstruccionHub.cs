using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Adaptadores.TiempoReal;

/// <summary>
/// Hub de notificaciones en tiempo real (sección 8.2). No decide si una operación es válida:
/// solo informa a los clientes conectados. Los grupos se autorizan igual que las consultas:
/// un Responsable solo se une a los grupos de sus proyectos, y la Autoridad y el Auditor a los
/// del desastre.
/// </summary>
[Authorize]
public sealed class ReconstruccionHub(IUsuarioActual usuario, ILecturaReconstruccion lectura) : Hub
{
    /// <summary>Nombre del método que reciben los clientes.</summary>
    public const string MetodoNotificacion = "Notificacion";

    public async Task UnirseADesastre(Guid desastreId)
    {
        if (usuario.Rol is not (Rol.Autoridad or Rol.Auditor))
        {
            throw new HubException("Solo la Autoridad y el Auditor se unen a los grupos del desastre (RN-RE-19).");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CatalogoNotificaciones.GrupoDesastre(desastreId));
    }

    public async Task UnirseAProyecto(Guid proyectoId)
    {
        switch (usuario.Rol)
        {
            case Rol.Autoridad or Rol.Auditor:
                break;
            case Rol.Responsable:
                var propios = await lectura.ListarProyectosDeUsuarioAsync(usuario.UsuarioId, Context.ConnectionAborted);
                if (!propios.Contains(proyectoId))
                {
                    throw new HubException("El Responsable solo se une a los grupos de sus proyectos (RN-RE-19).");
                }

                break;
            default:
                throw new HubException("El rol no puede unirse a grupos de proyecto (RN-RE-19).");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, CatalogoNotificaciones.GrupoProyecto(proyectoId));
    }

    public Task SalirDeDesastre(Guid desastreId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CatalogoNotificaciones.GrupoDesastre(desastreId));

    public Task SalirDeProyecto(Guid proyectoId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, CatalogoNotificaciones.GrupoProyecto(proyectoId));
}

/// <summary>
/// Implementación de <see cref="INotificadorTiempoReal"/> sobre SignalR. Envía solo
/// identificador, tipo y versión; el cliente consulta el detalle por gRPC (sección 8.2).
/// </summary>
public sealed class NotificadorSignalR(IHubContext<ReconstruccionHub> hub) : INotificadorTiempoReal
{
    public Task NotificarAsync(Notificacion notificacion, CancellationToken cancelacion = default) =>
        hub.Clients.Groups(notificacion.Grupos).SendAsync(ReconstruccionHub.MetodoNotificacion,
            new { notificacion.Tipo, notificacion.ObjetoId, notificacion.Version }, cancelacion);
}
