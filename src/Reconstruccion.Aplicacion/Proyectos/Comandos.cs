using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Proyectos;

// ---------------------------------------------------------------------------
// DTO de entrada compartidos
// ---------------------------------------------------------------------------

/// <summary>Aporte de financiación; cada aporte identifica una fuente distinta (RN-RE-06).</summary>
public sealed record AporteDto(string Fuente, decimal Monto);

/// <summary>Responsable de un organismo participante (RN-RE-06).</summary>
public sealed record ResponsableDto(Guid UsuarioId, Guid OrganismoId);

public sealed record ContratistaDto(string Identificacion, string Nombre);

/// <summary>Hito del cronograma; los pesos son positivos y suman 100 (RN-RE-07).</summary>
public sealed record HitoDto(string Nombre, DateOnly FechaInicio, DateOnly FechaFin, decimal Peso);

/// <summary>Referencia a una evidencia; el archivo binario no se guarda en este proyecto (RN-RE-10).</summary>
public sealed record ReferenciaEvidenciaDto(string Identificador, string Tipo, string Ubicacion);

// ---------------------------------------------------------------------------
// Ciclo de vida (Grupo B)
// ---------------------------------------------------------------------------

/// <summary>
/// Crea un proyecto en Propuesto con necesidades Pendientes del plan (RN-RE-01, 03, 05).
/// Quien llama queda como formulador (RN-RE-19). Modifica el plan: verifica su versión.
/// </summary>
public sealed record CrearProyecto(
    Guid RequestId,
    Guid DesastreId,
    long ExpectedVersionPlan,
    string Nombre,
    string Descripcion,
    TipoProyecto Tipo,
    IReadOnlyList<Guid> NecesidadIds) : IComandoConVersionDePlan;

/// <summary>Edición directa de presupuesto y aportes, solo en Propuesto y por el formulador (RN-RE-08, RN-RE-19).</summary>
public sealed record EditarPresupuestoPropuesto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    decimal Presupuesto,
    IReadOnlyList<AporteDto> Aportes) : IComandoConVersion;

/// <summary>Propuesto a Aprobado; aprobador Autoridad distinto del formulador (RN-RE-06).</summary>
public sealed record AprobarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion) : IComandoConVersion;

/// <summary>Propuesto a Rechazado; las necesidades vuelven a Pendiente (RN-RE-03, RN-RE-14).</summary>
public sealed record RechazarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    long ExpectedVersionPlan,
    string Motivo) : IComandoConVersion, IComandoConVersionDePlan;

/// <summary>Aprobado a EnEjecucion; requiere contratista y cronograma válidos (RN-RE-07).</summary>
public sealed record IniciarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion) : IComandoConVersion;

/// <summary>EnEjecucion a Suspendido, con motivo (RN-RE-13).</summary>
public sealed record SuspenderProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    string Motivo) : IComandoConVersion;

/// <summary>Suspendido a EnEjecucion, sin alerta de origen activa (RN-RE-20); revisa hitos vencidos.</summary>
public sealed record ReanudarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion) : IComandoConVersion;

/// <summary>Aprobado, EnEjecucion o Suspendido a Cancelado; las necesidades vuelven a Pendiente (RN-RE-03, RN-RE-13).</summary>
public sealed record CancelarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    long ExpectedVersionPlan,
    string Motivo) : IComandoConVersion, IComandoConVersionDePlan;

/// <summary>EnEjecucion a Finalizado; las necesidades pasan a Atendida (RN-RE-12).</summary>
public sealed record FinalizarProyecto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    long ExpectedVersionPlan) : IComandoConVersion, IComandoConVersionDePlan;

// ---------------------------------------------------------------------------
// Gestión operativa (Grupo C)
// ---------------------------------------------------------------------------

/// <summary>Asigna responsable y/o contratista; un campo nulo no cambia ese participante.</summary>
public sealed record AsignarParticipantes(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    ResponsableDto? Responsable,
    ContratistaDto? Contratista) : IComandoConVersion;

/// <summary>Define el cronograma en Propuesto o Aprobado (RN-RE-07).</summary>
public sealed record DefinirCronograma(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    IReadOnlyList<HitoDto> Hitos) : IComandoConVersion;

/// <summary>Reprograma las fechas de un hito, con motivo; los pesos quedan fijos (RN-RE-07).</summary>
public sealed record ReprogramarHito(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    Guid HitoId,
    DateOnly NuevaFechaInicio,
    DateOnly NuevaFechaFin,
    string Motivo) : IComandoConVersion;

/// <summary>
/// Registra el avance de un hito (RN-RE-10, RN-RE-17). Un porcentaje menor al vigente
/// exige <paramref name="MotivoCorreccion"/>.
/// </summary>
public sealed record RegistrarAvance(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    Guid HitoId,
    decimal Porcentaje,
    DateOnly FechaEfectiva,
    IReadOnlyList<ReferenciaEvidenciaDto> Evidencias,
    string? MotivoCorreccion = null) : IComandoConVersion;

/// <summary>
/// Registra un costo (RN-RE-11, RN-RE-17). Un costo nunca se borra: para corregirlo se
/// registra un reverso indicando <paramref name="CostoRevertidoId"/> y el motivo.
/// </summary>
public sealed record RegistrarCosto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    decimal Monto,
    string Concepto,
    DateOnly FechaEfectiva,
    Guid? CostoRevertidoId = null,
    string? MotivoReverso = null) : IComandoConVersion;

/// <summary>Adjunta la referencia a un documento; en estados terminales solo administrativos (RN-RE-14).</summary>
public sealed record AdjuntarDocumento(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    TipoDocumento Tipo,
    string Nombre,
    string Ubicacion) : IComandoConVersion;

// ---------------------------------------------------------------------------
// Gestión presupuestal (Grupo D)
// ---------------------------------------------------------------------------

/// <summary>
/// Solicita un cambio de presupuesto desde Aprobado (RN-RE-08, RN-RE-09). No altera el
/// presupuesto vigente hasta que se aprueba.
/// </summary>
public sealed record SolicitarCambioPresupuesto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    decimal NuevoPresupuesto,
    IReadOnlyList<AporteDto> NuevosAportes,
    string Motivo) : IComandoConVersion;

/// <summary>Aprueba o rechaza un cambio Solicitado; Autoridad distinta del solicitante (RN-RE-08).</summary>
public sealed record ResolverCambioPresupuesto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    Guid CambioId,
    bool Aprobar,
    string? Motivo) : IComandoConVersion;

/// <summary>El solicitante retira un cambio Solicitado; queda Anulado (RN-RE-09).</summary>
public sealed record RetirarCambioPresupuesto(
    Guid RequestId,
    Guid ProyectoId,
    long ExpectedVersion,
    Guid CambioId,
    string Motivo) : IComandoConVersion;
