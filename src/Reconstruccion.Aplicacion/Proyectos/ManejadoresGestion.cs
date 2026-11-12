using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Aplicacion.Proyectos;

// Transiciones complementarias (sección 5.1) y reprogramación de hitos.

/// <summary>
/// Propuesto a Rechazado; rol Autoridad. Las necesidades vuelven a Pendiente en la misma
/// unidad de trabajo (RN-RE-03, RN-RE-14, sección 7).
/// </summary>
public sealed class RechazarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes, IRepositorioProyectos proyectos)
    : IManejadorComando<RechazarProyecto, ResultadoComandoConPlan>
{
    public Task<Resultado<ResultadoComandoConPlan>> ManejarAsync(RechazarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);
            var plan = await Carga.PlanAsync(planes, proyecto.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);

            proyecto.Rechazar(comando.Motivo, ctx.Ahora);
            plan.LiberarNecesidades(proyecto.Id);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"Propuesto -> Rechazado. Motivo: {proyecto.UltimoMotivo}");
            ctx.Auditar("Plan", plan.Id, plan.DesastreId, $"{proyecto.NecesidadIds.Count} necesidades liberadas a Pendiente.");
            return new ResultadoComandoConPlan(proyecto.Id, proyecto.Version, plan.Version);
        }, cancelacion);
}

/// <summary>EnEjecucion a Suspendido con motivo; Responsable asignado o Autoridad (RN-RE-13).</summary>
public sealed class SuspenderProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<SuspenderProyecto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(SuspenderProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperadorOAutoridad(ctx.Usuario, proyecto);

            proyecto.Suspender(comando.Motivo, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"EnEjecucion -> Suspendido. Motivo: {proyecto.UltimoMotivo}");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>
/// Suspendido a EnEjecucion sin alerta de origen activa; revisa los hitos vencidos durante la
/// suspensión. Responsable asignado o Autoridad (RN-RE-20, RN-RE-21).
/// </summary>
public sealed class ReanudarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<ReanudarProyecto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(ReanudarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperadorOAutoridad(ctx.Usuario, proyecto);

            var alertasAntes = proyecto.Alertas.Count;
            proyecto.Reanudar(ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Suspendido -> EnEjecucion; {proyecto.Alertas.Count - alertasAntes} hitos vencidos durante la suspensión.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>
/// Aprobado, EnEjecucion o Suspendido a Cancelado; rol Autoridad. Anula el cambio Solicitado
/// y libera las necesidades en la misma unidad de trabajo (RN-RE-03, 09 y 13).
/// </summary>
public sealed class CancelarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes, IRepositorioProyectos proyectos)
    : IManejadorComando<CancelarProyecto, ResultadoComandoConPlan>
{
    public Task<Resultado<ResultadoComandoConPlan>> ManejarAsync(CancelarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);
            var plan = await Carga.PlanAsync(planes, proyecto.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);

            var estadoAnterior = proyecto.Estado;
            proyecto.Cancelar(comando.Motivo, ctx.Ahora);
            plan.LiberarNecesidades(proyecto.Id);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"{estadoAnterior} -> Cancelado. Motivo: {proyecto.UltimoMotivo}");
            ctx.Auditar("Plan", plan.Id, plan.DesastreId, $"{proyecto.NecesidadIds.Count} necesidades liberadas a Pendiente.");
            return new ResultadoComandoConPlan(proyecto.Id, proyecto.Version, plan.Version);
        }, cancelacion);
}

/// <summary>Reprograma las fechas de un hito con motivo; Responsable asignado (RN-RE-07, RN-RE-21).</summary>
public sealed class ReprogramarHitoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<ReprogramarHito, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(ReprogramarHito comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);
            var hito = proyecto.Hitos.FirstOrDefault(h => h.Id == comando.HitoId);
            var fechasAnteriores = hito is null ? "" : $"{hito.FechaInicio:yyyy-MM-dd} a {hito.FechaFin:yyyy-MM-dd}";

            proyecto.ReprogramarHito(comando.HitoId, comando.NuevaFechaInicio, comando.NuevaFechaFin, comando.Motivo, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Hito {comando.HitoId}: {fechasAnteriores} -> {comando.NuevaFechaInicio:yyyy-MM-dd} a {comando.NuevaFechaFin:yyyy-MM-dd}. Motivo: {comando.Motivo}");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}
