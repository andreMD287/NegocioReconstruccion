using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Integracion;

/// <summary>
/// Tabla 8.2 del diseño: qué grupo SignalR recibe cada evento. La notificación lleva solo
/// identificador, tipo y versión; el cliente consulta el detalle por gRPC.
/// Los eventos de proyecto se identifican por el proyecto y los de plan por el desastre.
/// </summary>
public static class CatalogoNotificaciones
{
    public static string GrupoDesastre(Guid desastreId) => $"desastre-{desastreId}";

    public static string GrupoProyecto(Guid proyectoId) => $"proyecto-{proyectoId}";

    /// <summary>Notificación del evento, o null si el evento no se notifica (por ejemplo DocumentoAdjuntado).</summary>
    public static Notificacion? Para(IEventoDominio evento)
    {
        var desastre = GrupoDesastre(evento.DesastreId);
        var proyecto = GrupoProyecto(evento.AgregadoId);

        string[]? grupos = evento switch
        {
            NecesidadesRecibidas or NecesidadDescartada or NecesidadReactivada => [desastre],
            PlanCompletado or PlanReabierto or PlanCerrado => [desastre],
            ProyectoCreado => [desastre],
            ParticipantesAsignados or AvanceRegistrado or HitoReprogramado or CambioPresupuestalResuelto => [proyecto],
            ProyectoEstadoCambiado or CostoRegistrado or CambioPresupuestalSolicitado => [desastre, proyecto],
            AlertaGenerada { EnPlan: true } or AlertaResuelta { EnPlan: true } => [desastre],
            AlertaGenerada or AlertaResuelta => [desastre, proyecto],
            DocumentoAdjuntado => null,
            _ => null
        };

        if (grupos is null)
        {
            return null;
        }

        var esDelPlan = evento is NecesidadesRecibidas or NecesidadDescartada or NecesidadReactivada
            or PlanCompletado or PlanReabierto or PlanCerrado
            or AlertaGenerada { EnPlan: true } or AlertaResuelta { EnPlan: true };

        return new Notificacion(evento.GetType().Name, esDelPlan ? evento.DesastreId : evento.AgregadoId,
            evento.VersionAgregado, grupos);
    }
}
