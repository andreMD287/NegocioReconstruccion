namespace Reconstruccion.Aplicacion.Integracion;

// Contrato de entrada desde Caracterización (sección 8.1, CaracterizacionService).
// Estos mensajes no llevan RequestId: la idempotencia se basa en EvaluacionId y
// VersionEvaluacion, y en la versión del ciclo para la señal de fin (RN-RE-18, RN-RE-22).

public sealed record UbicacionDto(double Latitud, double Longitud, string? Descripcion);

/// <summary>
/// Evaluación que Caracterización envía como validada y con intervención requerida (RN-RE-02).
/// El nivel de daño se conserva como texto, sin lista fija de categorías.
/// </summary>
public sealed record EvaluacionRecibidaDto(
    Guid EvaluacionId,
    long VersionEvaluacion,
    Guid ActivoId,
    string TipoActivo,
    string NivelDano,
    DateTimeOffset FechaValidacion,
    UbicacionDto Ubicacion,
    bool Validada,
    bool RequiereIntervencion);

/// <summary>Entrega de evaluaciones de un ciclo de caracterización (RecibirNecesidades).</summary>
public sealed record RecibirNecesidades(
    Guid DesastreId,
    int VersionCiclo,
    IReadOnlyList<EvaluacionRecibidaDto> Evaluaciones);

public enum MotivoEvaluacionNoAceptada
{
    /// <summary>Mismo EvaluacionId con versión ya recibida: no se duplica (RN-RE-18).</summary>
    Repetida,

    /// <summary>Versión menor a la vigente: se registra como obsoleta (RN-RE-18).</summary>
    Obsoleta,

    /// <summary>No cumple el contrato de RN-RE-02 (no validada, sin intervención o sin desastre).</summary>
    ContratoIncumplido,

    /// <summary>El plan del desastre está Cerrado: se rechaza y se audita (RN-RE-05).</summary>
    PlanCerrado
}

public sealed record EvaluacionNoAceptada(Guid EvaluacionId, MotivoEvaluacionNoAceptada Motivo);

public sealed record ResultadoRecepcionNecesidades(
    IReadOnlyList<Guid> NecesidadesCreadas,
    IReadOnlyList<EvaluacionNoAceptada> NoAceptadas,
    long VersionPlan);

public enum TipoCambioEvaluacion
{
    Correccion,
    Revocacion
}

/// <summary>
/// Corrección o revocación de una evaluación ya enviada (RN-RE-20). En una corrección,
/// <paramref name="DatosCorregidos"/> trae la nueva copia; en una revocación es nulo.
/// </summary>
public sealed record RecibirCambioEvaluacion(
    Guid DesastreId,
    Guid EvaluacionId,
    long VersionEvaluacion,
    TipoCambioEvaluacion Tipo,
    EvaluacionRecibidaDto? DatosCorregidos);

public sealed record ResultadoCambioEvaluacion(
    Guid? NecesidadId,
    bool AlertaGenerada,
    bool RegistradoComoRectificacion,
    bool Obsoleto,
    long VersionPlan);

/// <summary>
/// Señal de fin de caracterización (RN-RE-22): versión del ciclo y cantidad de evaluaciones
/// con intervención requerida de ese ciclo.
/// </summary>
public sealed record RecibirFinCaracterizacion(
    Guid DesastreId,
    int VersionCiclo,
    int CantidadEsperada);

public enum RespuestaFinCaracterizacion
{
    /// <summary>Se recibieron todas las evaluaciones del ciclo; la señal queda aceptada.</summary>
    Aceptada,

    /// <summary>Faltan evaluaciones del ciclo; Caracterización debe reintentar.</summary>
    FaltanEvaluaciones,

    /// <summary>Cantidad cero y no existe plan: Caracterización usa su camino directo a Cierre.</summary>
    NoHayPlanAplicable,

    /// <summary>Versión de ciclo menor o igual a la ya aceptada.</summary>
    Obsoleta
}

public sealed record ResultadoFinCaracterizacion(
    RespuestaFinCaracterizacion Respuesta,
    int EvaluacionesRecibidas,
    Dominio.Comun.EstadoPlan? EstadoPlan,
    long? VersionPlan);
