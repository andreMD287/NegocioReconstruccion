namespace Reconstruccion.Dominio.Planes;

/// <summary>Evaluación entregada por Caracterización, con las marcas del contrato (RN-RE-02).</summary>
public sealed record EvaluacionEntrante(EvaluacionOrigen Origen, bool Validada, bool RequiereIntervencion);

public enum ResultadoRecepcion
{
    Creada,
    Repetida,
    Obsoleta,
    ContratoIncumplido,
    PlanCerrado
}

/// <param name="ProyectoAfectadoId">
/// Si una versión mayor corrigió una necesidad asignada, el proyecto que debe registrar
/// la alerta de origen modificado (RN-RE-20).
/// </param>
public sealed record RecepcionEvaluacion(Guid EvaluacionId, ResultadoRecepcion Resultado, Guid? NecesidadId, Guid? ProyectoAfectadoId = null);

public enum TipoCambioOrigen
{
    Correccion,
    Revocacion
}

/// <summary>
/// Resultado de aplicar una corrección o revocación (RN-RE-20). Si la necesidad está
/// asignada, <see cref="ProyectoAfectadoId"/> indica el proyecto que debe registrar la
/// alerta de origen modificado.
/// </summary>
public sealed record ResultadoCambioOrigen(
    Guid? NecesidadId,
    bool AlertaGeneradaEnPlan,
    Guid? ProyectoAfectadoId,
    bool RegistradoComoRectificacion,
    bool Obsoleto);

public enum ResultadoSenalFin
{
    Aceptada,
    FaltanEvaluaciones,
    Obsoleta
}

public sealed record RespuestaSenalFin(ResultadoSenalFin Resultado, int EvaluacionesRecibidas);
