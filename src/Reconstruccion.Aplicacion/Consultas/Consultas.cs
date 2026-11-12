using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Consultas;

// ---------------------------------------------------------------------------
// Consultas (ConsultasService, sección 8.1)
// ---------------------------------------------------------------------------

/// <summary>Seguimiento consolidado por desastre; rol Autoridad (RF-RE-06).</summary>
public sealed record ConsultarSeguimientoConsolidado(Guid DesastreId) : IConsulta;

/// <summary>Detalle de un proyecto; el Responsable solo ve sus proyectos asignados.</summary>
public sealed record ConsultarDetalleProyecto(Guid ProyectoId) : IConsulta;

/// <summary>
/// Historial de auditoría; Auditor y Autoridad, y el Responsable solo sobre sus proyectos.
/// Los filtros nulos no restringen.
/// </summary>
public sealed record ConsultarHistorialAuditoria(
    Guid? DesastreId,
    Guid? ObjetoId,
    DateTimeOffset? Desde,
    DateTimeOffset? Hasta,
    int Pagina = 1,
    int TamanoPagina = 50) : IConsulta;

// ---------------------------------------------------------------------------
// Vistas de lectura
// ---------------------------------------------------------------------------

/// <summary>
/// Seguimiento consolidado (sección 4). El avance ponderado se calcula como
/// Σ presupuesto × avance / Σ presupuesto.
/// </summary>
public sealed record SeguimientoConsolidadoDto(
    Guid DesastreId,
    EstadoPlan EstadoPlan,
    long VersionPlan,
    IReadOnlyDictionary<EstadoProyecto, int> ProyectosPorEstado,
    decimal PresupuestoVigente,
    decimal CostoEjecutado,
    decimal AvancePonderado,
    IReadOnlyDictionary<EstadoNecesidad, int> NecesidadesPorEstado,
    IReadOnlyDictionary<TipoAlerta, int> AlertasActivas);

public sealed record HitoVistaDto(
    Guid HitoId,
    string Nombre,
    DateOnly FechaInicio,
    DateOnly FechaFin,
    decimal Peso,
    decimal Avance);

public sealed record CambioPresupuestalVistaDto(
    Guid CambioId,
    EstadoCambioPresupuestal Estado,
    decimal PresupuestoAnterior,
    decimal PresupuestoNuevo,
    Guid SolicitanteId,
    Guid? ResolutorId,
    DateTimeOffset FechaSolicitud,
    DateTimeOffset? FechaResolucion,
    string Motivo);

public sealed record AlertaVistaDto(Guid AlertaId, TipoAlerta Tipo, EstadoAlerta Estado, DateTimeOffset Fecha, string Descripcion);

public sealed record DocumentoVistaDto(Guid DocumentoId, TipoDocumento Tipo, string Nombre, string Ubicacion, DateTimeOffset FechaRegistro);

public sealed record DetalleProyectoDto(
    Guid ProyectoId,
    Guid DesastreId,
    string Nombre,
    string Descripcion,
    TipoProyecto Tipo,
    EstadoProyecto Estado,
    long Version,
    Guid FormuladorId,
    Guid? ResponsableId,
    string? Contratista,
    decimal PresupuestoVigente,
    decimal CostoEjecutado,
    decimal Avance,
    IReadOnlyList<Guid> NecesidadIds,
    IReadOnlyList<HitoVistaDto> Hitos,
    IReadOnlyList<CambioPresupuestalVistaDto> CambiosPresupuestales,
    IReadOnlyList<AlertaVistaDto> Alertas,
    IReadOnlyList<DocumentoVistaDto> Documentos);

public sealed record EntradaAuditoriaDto(
    DateTimeOffset Fecha,
    Guid UsuarioId,
    Rol Rol,
    string Accion,
    string TipoObjeto,
    Guid ObjetoId,
    ResultadoOperacion Resultado,
    string? Detalle);

public sealed record Pagina<T>(IReadOnlyList<T> Elementos, int NumeroPagina, int TamanoPagina, int Total);

// ---------------------------------------------------------------------------
// Puerto de lectura
// ---------------------------------------------------------------------------

/// <summary>
/// Lecturas para las consultas. Lo implementa Infraestructura sobre la misma base
/// (sección 9.5: sin modelo de lectura separado). La autorización la aplica el caso de uso.
/// </summary>
public interface ILecturaReconstruccion
{
    Task<SeguimientoConsolidadoDto?> ObtenerSeguimientoAsync(Guid desastreId, CancellationToken cancelacion = default);

    Task<DetalleProyectoDto?> ObtenerDetalleProyectoAsync(Guid proyectoId, CancellationToken cancelacion = default);

    /// <param name="proyectosPermitidos">Si no es nulo, limita el historial a esos proyectos (Responsable).</param>
    Task<Pagina<EntradaAuditoriaDto>> ObtenerHistorialAsync(
        ConsultarHistorialAuditoria filtro,
        IReadOnlyCollection<Guid>? proyectosPermitidos,
        CancellationToken cancelacion = default);

    /// <summary>Proyectos donde el usuario es el Responsable vigente o el formulador (RN-RE-19).</summary>
    Task<IReadOnlyList<Guid>> ListarProyectosDeUsuarioAsync(Guid usuarioId, CancellationToken cancelacion = default);
}
