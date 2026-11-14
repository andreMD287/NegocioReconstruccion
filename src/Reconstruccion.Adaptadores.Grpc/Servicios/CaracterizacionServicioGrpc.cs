using Grpc.Core;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Contratos.V1;
using Reconstruccion.Dominio.Comun;
using static Reconstruccion.Adaptadores.Grpc.Servicios.TraduccionGrpc;
using Integracion = Reconstruccion.Aplicacion.Integracion;

namespace Reconstruccion.Adaptadores.Grpc.Servicios;

/// <summary>
/// CaracterizacionService (sección 8.1): contrato de entrada desde Caracterización.
/// No lleva RequestId; la idempotencia se basa en EvaluacionId, VersionEvaluacion y la
/// versión del ciclo (RN-RE-18, RN-RE-22).
/// </summary>
public sealed class CaracterizacionServicioGrpc(
    IManejadorComando<Integracion.RecibirNecesidades, ResultadoRecepcionNecesidades> recibirNecesidades,
    IManejadorComando<Integracion.RecibirCambioEvaluacion, ResultadoCambioEvaluacion> recibirCambio,
    IManejadorComando<Integracion.RecibirFinCaracterizacion, ResultadoFinCaracterizacion> recibirFin)
    : CaracterizacionService.CaracterizacionServiceBase
{
    public override async Task<RecibirNecesidadesResponse> RecibirNecesidades(RecibirNecesidadesRequest r, ServerCallContext c)
    {
        var resultado = Valor(await recibirNecesidades.ManejarAsync(new Integracion.RecibirNecesidades(
            Id(r.DesastreId, "desastre_id"), r.VersionCiclo,
            r.Evaluaciones.Select(e => Evaluacion(e, "evaluaciones")).ToList()), c.CancellationToken));

        var respuesta = new RecibirNecesidadesResponse { VersionPlan = resultado.VersionPlan };
        respuesta.NecesidadesCreadas.AddRange(resultado.NecesidadesCreadas.Select(id => id.ToString()));
        respuesta.NoAceptadas.AddRange(resultado.NoAceptadas.Select(n => new EvaluacionNoAceptadaGrpc
        {
            EvaluacionId = n.EvaluacionId.ToString(),
            Motivo = EnumGrpc<MotivoEvaluacionNoAceptadaGrpc>(n.Motivo)
        }));
        return respuesta;
    }

    public override async Task<RecibirCambioEvaluacionResponse> RecibirCambioEvaluacion(RecibirCambioEvaluacionRequest r, ServerCallContext c)
    {
        var tipo = Enum<TipoCambioEvaluacion>(r.Tipo, "tipo");
        if (tipo == TipoCambioEvaluacion.Correccion && r.DatosCorregidos is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Una corrección debe traer datos_corregidos."));
        }

        var resultado = Valor(await recibirCambio.ManejarAsync(new Integracion.RecibirCambioEvaluacion(
            Id(r.DesastreId, "desastre_id"), Id(r.EvaluacionId, "evaluacion_id"), r.VersionEvaluacion, tipo,
            r.DatosCorregidos is null ? null : Evaluacion(r.DatosCorregidos, "datos_corregidos")), c.CancellationToken));

        var respuesta = new RecibirCambioEvaluacionResponse
        {
            AlertaGenerada = resultado.AlertaGenerada,
            RegistradoComoRectificacion = resultado.RegistradoComoRectificacion,
            Obsoleto = resultado.Obsoleto,
            VersionPlan = resultado.VersionPlan
        };
        if (resultado.NecesidadId is { } necesidad)
        {
            respuesta.NecesidadId = necesidad.ToString();
        }

        return respuesta;
    }

    public override async Task<RecibirFinCaracterizacionResponse> RecibirFinCaracterizacion(RecibirFinCaracterizacionRequest r, ServerCallContext c)
    {
        var resultado = Valor(await recibirFin.ManejarAsync(new Integracion.RecibirFinCaracterizacion(
            Id(r.DesastreId, "desastre_id"), r.VersionCiclo, r.CantidadEsperada), c.CancellationToken));

        var respuesta = new RecibirFinCaracterizacionResponse
        {
            Respuesta = EnumGrpc<RespuestaFinCaracterizacionGrpc>(resultado.Respuesta),
            EvaluacionesRecibidas = resultado.EvaluacionesRecibidas
        };
        if (resultado.EstadoPlan is { } estado)
        {
            respuesta.EstadoPlan = EnumGrpc<EstadoPlanGrpc>(estado);
        }

        if (resultado.VersionPlan is { } version)
        {
            respuesta.VersionPlan = version;
        }

        return respuesta;
    }

    private static EvaluacionRecibidaDto Evaluacion(EvaluacionRecibida e, string campo) =>
        new(Id(e.EvaluacionId, $"{campo}.evaluacion_id"), e.VersionEvaluacion, Id(e.ActivoId, $"{campo}.activo_id"),
            e.TipoActivo, e.NivelDano, Instante(e.FechaValidacion, $"{campo}.fecha_validacion"),
            e.Ubicacion is null
                ? throw new RpcException(new Status(StatusCode.InvalidArgument, $"{campo}.ubicacion es obligatoria."))
                : new UbicacionDto(e.Ubicacion.Latitud, e.Ubicacion.Longitud, e.Ubicacion.HasDescripcion ? e.Ubicacion.Descripcion : null),
            e.Validada, e.RequiereIntervencion);
}
