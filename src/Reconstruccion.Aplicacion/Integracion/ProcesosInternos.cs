namespace Reconstruccion.Aplicacion.Integracion;

// Comandos que no expone el contrato gRPC: los invocan procesos del Host
// (publicador del outbox y VigilanciaCronograma) con el rol Sistema.

/// <summary>
/// El publicador del outbox lo invoca cuando Cierre acepta la notificación. El plan pasa
/// a Cerrado solo si la versión aceptada sigue siendo la vigente (RN-RE-15).
/// </summary>
public sealed record RegistrarAceptacionCierre(Guid DesastreId, long VersionPlanAceptada);

public sealed record ResultadoAceptacionCierre(bool PlanCerrado, long VersionPlan);

/// <summary>
/// Ejecución periódica de VigilanciaCronograma: genera alertas de hito vencido en
/// proyectos EnEjecucion sin cambiar su estado (RN-RE-21).
/// </summary>
public sealed record EjecutarVigilanciaCronograma;

public sealed record ResultadoVigilanciaCronograma(int ProyectosRevisados, int AlertasGeneradas);
