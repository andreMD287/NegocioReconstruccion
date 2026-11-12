using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;

namespace Reconstruccion.Aplicacion.Integracion;

/// <summary>
/// RecibirNecesidades: crea el plan del desastre si no existe y registra las evaluaciones
/// del ciclo (RN-RE-02, 04, 05, 18 y 22). Si una versión mayor corrige una necesidad ya
/// asignada, registra la alerta de origen en su proyecto (RN-RE-20).
/// </summary>
public sealed class RecibirNecesidadesManejador(
    EjecutorOperaciones ejecutor,
    IRepositorioPlanes planes,
    IRepositorioProyectos proyectos)
    : IManejadorComando<RecibirNecesidades, ResultadoRecepcionNecesidades>
{
    public Task<Resultado<ResultadoRecepcionNecesidades>> ManejarAsync(RecibirNecesidades comando,
        CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Caracterizacion);
            if (comando.DesastreId == Guid.Empty)
            {
                throw new DatosInvalidosException("El desastre es obligatorio (RN-RE-02).");
            }

            var plan = await planes.ObtenerPorDesastreAsync(comando.DesastreId, cancelacion);
            if (plan is null)
            {
                plan = PlanReconstruccion.Abrir(Guid.CreateVersion7(), comando.DesastreId);
                planes.Agregar(plan);
            }

            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);
            ctx.Seguir(plan);

            var entrantes = comando.Evaluaciones.Select(Mapeo.AEntrante).ToList();
            var recepciones = plan.RecibirEvaluaciones(comando.VersionCiclo, entrantes, Guid.CreateVersion7, ctx.Ahora);

            foreach (var afectada in recepciones.Where(r => r.ProyectoAfectadoId is not null))
            {
                await RegistrarAlertaEnProyectoAsync(ctx, afectada.ProyectoAfectadoId!.Value, afectada.NecesidadId!.Value, cancelacion);
            }

            var creadas = recepciones.Where(r => r.Resultado == ResultadoRecepcion.Creada).Select(r => r.NecesidadId!.Value).ToList();
            var noAceptadas = recepciones
                .Where(r => r.Resultado != ResultadoRecepcion.Creada)
                .Select(r => new EvaluacionNoAceptada(r.EvaluacionId, Mapeo.AMotivo(r.Resultado)))
                .ToList();

            ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                $"Ciclo {comando.VersionCiclo}: {creadas.Count} necesidades creadas, {noAceptadas.Count} no aceptadas.");
            if (plan.Estado == EstadoPlan.Cerrado && comando.Evaluaciones.Count > 0)
            {
                // RN-RE-05: con el plan Cerrado la necesidad se rechaza y se audita.
                ctx.Auditar("Plan", plan.Id, plan.DesastreId, "Evaluaciones rechazadas: el plan está Cerrado (RN-RE-05).");
            }

            return new ResultadoRecepcionNecesidades(creadas, noAceptadas, plan.Version);
        }, cancelacion);

    private async Task RegistrarAlertaEnProyectoAsync(ContextoOperacion ctx, Guid proyectoId, Guid necesidadId,
        CancellationToken cancelacion)
    {
        var proyecto = await proyectos.ObtenerPorIdAsync(proyectoId, cancelacion)
            ?? throw new NoEncontradoException($"El proyecto {proyectoId} no existe.");
        ctx.Seguir(proyecto).RegistrarAlertaOrigen(necesidadId, ctx.Ahora);
        ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"Alerta de origen modificado por la necesidad {necesidadId}.");
    }
}

/// <summary>
/// RecibirCambioEvaluacion: corrección o revocación de una evaluación ya enviada (RN-RE-20).
/// </summary>
public sealed class RecibirCambioEvaluacionManejador(
    EjecutorOperaciones ejecutor,
    IRepositorioPlanes planes,
    IRepositorioProyectos proyectos)
    : IManejadorComando<RecibirCambioEvaluacion, ResultadoCambioEvaluacion>
{
    public Task<Resultado<ResultadoCambioEvaluacion>> ManejarAsync(RecibirCambioEvaluacion comando,
        CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Caracterizacion);
            var plan = await planes.ObtenerPorDesastreAsync(comando.DesastreId, cancelacion)
                ?? throw new NoEncontradoException($"No existe plan para el desastre {comando.DesastreId}.");
            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);
            ctx.Seguir(plan);

            var tipo = comando.Tipo == TipoCambioEvaluacion.Correccion ? TipoCambioOrigen.Correccion : TipoCambioOrigen.Revocacion;
            var corregida = comando.DatosCorregidos is null ? null : Mapeo.AEntrante(comando.DatosCorregidos).Origen;
            var resultado = plan.AplicarCambioEvaluacion(comando.EvaluacionId, comando.VersionEvaluacion, tipo, corregida, ctx.Ahora);

            var alertaGenerada = resultado.AlertaGeneradaEnPlan;
            if (resultado.ProyectoAfectadoId is { } proyectoId)
            {
                var proyecto = await proyectos.ObtenerPorIdAsync(proyectoId, cancelacion)
                    ?? throw new NoEncontradoException($"El proyecto {proyectoId} no existe.");
                ctx.Seguir(proyecto).RegistrarAlertaOrigen(resultado.NecesidadId!.Value, ctx.Ahora);
                ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, "Alerta de origen modificado.");
                alertaGenerada = true;
            }

            if (!resultado.Obsoleto)
            {
                ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                    resultado.RegistradoComoRectificacion
                        ? $"Rectificación de la evaluación {comando.EvaluacionId} (RN-RE-14)."
                        : $"{comando.Tipo} de la evaluación {comando.EvaluacionId}.");
            }

            return new ResultadoCambioEvaluacion(resultado.NecesidadId, alertaGenerada,
                resultado.RegistradoComoRectificacion, resultado.Obsoleto, plan.Version);
        }, cancelacion);
}

/// <summary>
/// RecibirFinCaracterizacion (RN-RE-22). Si no existe plan y la cantidad es cero, responde
/// que no hay plan aplicable y no crea un plan vacío.
/// </summary>
public sealed class RecibirFinCaracterizacionManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes)
    : IManejadorComando<RecibirFinCaracterizacion, ResultadoFinCaracterizacion>
{
    public Task<Resultado<ResultadoFinCaracterizacion>> ManejarAsync(RecibirFinCaracterizacion comando,
        CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Caracterizacion);
            var plan = await planes.ObtenerPorDesastreAsync(comando.DesastreId, cancelacion);
            if (plan is null)
            {
                ctx.FijarObjetivo("Plan", Guid.Empty, comando.DesastreId);
                var respuestaSinPlan = comando.CantidadEsperada == 0
                    ? RespuestaFinCaracterizacion.NoHayPlanAplicable
                    : RespuestaFinCaracterizacion.FaltanEvaluaciones;
                ctx.Auditar("Plan", Guid.Empty, comando.DesastreId, $"Señal de fin del ciclo {comando.VersionCiclo}: {respuestaSinPlan}.");
                return new ResultadoFinCaracterizacion(respuestaSinPlan, 0, null, null);
            }

            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);
            ctx.Seguir(plan);
            var respuesta = plan.RecibirSenalFin(comando.VersionCiclo, comando.CantidadEsperada, ctx.Ahora);
            var mapeada = respuesta.Resultado switch
            {
                ResultadoSenalFin.Aceptada => RespuestaFinCaracterizacion.Aceptada,
                ResultadoSenalFin.FaltanEvaluaciones => RespuestaFinCaracterizacion.FaltanEvaluaciones,
                _ => RespuestaFinCaracterizacion.Obsoleta
            };

            ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                $"Señal de fin del ciclo {comando.VersionCiclo}: {mapeada}; plan {plan.Estado}.");
            return new ResultadoFinCaracterizacion(mapeada, respuesta.EvaluacionesRecibidas, plan.Estado, plan.Version);
        }, cancelacion);
}

/// <summary>
/// El publicador del outbox informa que Cierre aceptó la notificación. El plan pasa a
/// Cerrado solo si la versión aceptada es la vigente (RN-RE-15).
/// </summary>
public sealed class RegistrarAceptacionCierreManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes)
    : IManejadorComando<RegistrarAceptacionCierre, ResultadoAceptacionCierre>
{
    public Task<Resultado<ResultadoAceptacionCierre>> ManejarAsync(RegistrarAceptacionCierre comando,
        CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Sistema);
            var plan = await planes.ObtenerPorDesastreAsync(comando.DesastreId, cancelacion)
                ?? throw new NoEncontradoException($"No existe plan para el desastre {comando.DesastreId}.");
            ctx.FijarObjetivo("Plan", plan.Id, plan.DesastreId);
            ctx.Seguir(plan);

            var cerrado = plan.RegistrarAceptacionCierre(comando.VersionPlanAceptada, ctx.Ahora);
            ctx.Auditar("Plan", plan.Id, plan.DesastreId, cerrado
                ? $"Cierre aceptó la versión {comando.VersionPlanAceptada}; plan Cerrado."
                : $"Cierre aceptó la versión {comando.VersionPlanAceptada}, ya superada; el plan sigue {plan.Estado}.");
            return new ResultadoAceptacionCierre(cerrado, plan.Version);
        }, cancelacion);
}

internal static class Mapeo
{
    public static EvaluacionEntrante AEntrante(EvaluacionRecibidaDto dto) =>
        new(new EvaluacionOrigen(dto.EvaluacionId, dto.VersionEvaluacion, dto.ActivoId,
                dto.TipoActivo?.Trim() ?? string.Empty, dto.NivelDano?.Trim() ?? string.Empty, dto.FechaValidacion,
                new Ubicacion(dto.Ubicacion.Latitud, dto.Ubicacion.Longitud, dto.Ubicacion.Descripcion)),
            dto.Validada, dto.RequiereIntervencion);

    public static MotivoEvaluacionNoAceptada AMotivo(ResultadoRecepcion resultado) => resultado switch
    {
        ResultadoRecepcion.Repetida => MotivoEvaluacionNoAceptada.Repetida,
        ResultadoRecepcion.Obsoleta => MotivoEvaluacionNoAceptada.Obsoleta,
        ResultadoRecepcion.PlanCerrado => MotivoEvaluacionNoAceptada.PlanCerrado,
        _ => MotivoEvaluacionNoAceptada.ContratoIncumplido
    };
}
