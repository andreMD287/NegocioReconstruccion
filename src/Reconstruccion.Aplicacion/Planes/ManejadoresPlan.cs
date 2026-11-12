using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Proyectos;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Aplicacion.Planes;

// Decisiones de la Autoridad sobre necesidades y alertas (secciones 4 y 5.2).
// Respuesta acordada en el contrato gRPC: Descartar y Reactivar devuelven
// id = necesidad y la versión del plan; ResolverAlerta devuelve id = alerta y la
// versión del agregado donde está la alerta.

/// <summary>
/// Descarta una necesidad Pendiente con justificación; rol Autoridad. Resuelve su alerta de
/// origen y el plan evalúa su completitud (RN-RE-15, RN-RE-20).
/// </summary>
public sealed class DescartarNecesidadManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes)
    : IManejadorComando<DescartarNecesidad, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(DescartarNecesidad comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            ctx.FijarObjetivo("Plan", Guid.Empty, comando.DesastreId);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);
            var plan = await Carga.PlanAsync(planes, comando.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);
            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);

            plan.DescartarNecesidad(comando.NecesidadId, comando.Justificacion, ctx.Ahora);

            ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                $"Necesidad {comando.NecesidadId} descartada. Justificación: {comando.Justificacion}; plan {plan.Estado}.");
            return new ResultadoComando(comando.NecesidadId, plan.Version);
        }, cancelacion);
}

/// <summary>Reactiva una necesidad Descartada con justificación; rol Autoridad. Puede reabrir el plan (RN-RE-15).</summary>
public sealed class ReactivarNecesidadManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes)
    : IManejadorComando<ReactivarNecesidad, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(ReactivarNecesidad comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            ctx.FijarObjetivo("Plan", Guid.Empty, comando.DesastreId);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);
            var plan = await Carga.PlanAsync(planes, comando.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);
            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);

            plan.ReactivarNecesidad(comando.NecesidadId, comando.Justificacion, ctx.Ahora);

            ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                $"Necesidad {comando.NecesidadId} reactivada. Justificación: {comando.Justificacion}; plan {plan.Estado}.");
            return new ResultadoComando(comando.NecesidadId, plan.Version);
        }, cancelacion);
}

/// <summary>
/// La Autoridad resuelve una alerta de origen modificado con justificación (RN-RE-20, RN-RE-21).
/// Si la alerta está en el plan, el plan evalúa su completitud.
/// </summary>
public sealed class ResolverAlertaManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes, IRepositorioProyectos proyectos)
    : IManejadorComando<ResolverAlerta, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(ResolverAlerta comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);

            if (comando.Ubicacion == UbicacionAlerta.Plan)
            {
                ctx.FijarObjetivo("Plan", Guid.Empty, comando.AgregadoId);
                var plan = await Carga.PlanAsync(planes, comando.AgregadoId, comando.ExpectedVersion, ctx, cancelacion);
                ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);
                plan.ResolverAlerta(comando.AlertaId, comando.Justificacion, ctx.Ahora);
                ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                    $"Alerta {comando.AlertaId} resuelta. Justificación: {comando.Justificacion}; plan {plan.Estado}.");
                return new ResultadoComando(comando.AlertaId, plan.Version);
            }

            var proyecto = await Carga.ProyectoAsync(proyectos, comando.AgregadoId, comando.ExpectedVersion, ctx, cancelacion);
            proyecto.ResolverAlerta(comando.AlertaId, comando.Justificacion, ctx.Ahora);
            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Alerta {comando.AlertaId} resuelta. Justificación: {comando.Justificacion}");
            return new ResultadoComando(comando.AlertaId, proyecto.Version);
        }, cancelacion);
}
