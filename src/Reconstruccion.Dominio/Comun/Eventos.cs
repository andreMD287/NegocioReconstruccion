namespace Reconstruccion.Dominio.Comun;

// Eventos de dominio de la tabla 8.2 del diseño. Cada uno lleva el desastre,
// el agregado que lo emitió y su versión después del cambio.

// --- Plan y necesidades ------------------------------------------------------

public sealed record NecesidadesRecibidas(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    IReadOnlyList<Guid> NecesidadIds) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record NecesidadDescartada(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid NecesidadId) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record NecesidadReactivada(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid NecesidadId) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record PlanCompletado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn)
    : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record PlanReabierto(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn)
    : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record PlanCerrado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn)
    : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

// --- Proyecto ----------------------------------------------------------------

public sealed record ProyectoCreado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    IReadOnlyList<Guid> NecesidadIds) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record ParticipantesAsignados(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn)
    : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record ProyectoEstadoCambiado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    EstadoProyecto EstadoAnterior, EstadoProyecto EstadoNuevo, string? Motivo)
    : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record AvanceRegistrado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid HitoId, decimal Porcentaje, decimal AvanceProyecto) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record CostoRegistrado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid CostoId, bool EsReverso) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record HitoReprogramado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid HitoId) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

/// <summary>Según la tabla 8.2 no genera notificación SignalR; queda en el outbox y la auditoría.</summary>
public sealed record DocumentoAdjuntado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid DocumentoId, TipoDocumento Tipo) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record CambioPresupuestalSolicitado(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid CambioId) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

public sealed record CambioPresupuestalResuelto(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid CambioId, EstadoCambioPresupuestal Estado) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

// --- Alertas (plan o proyecto) -------------------------------------------------

/// <param name="EnPlan">La alerta está en el plan (necesidad no asignada) y no en un proyecto.</param>
public sealed record AlertaGenerada(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid AlertaId, TipoAlerta Tipo, Guid ObjetoId, bool EnPlan = false) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);

/// <param name="EnPlan">La alerta está en el plan (necesidad no asignada) y no en un proyecto.</param>
public sealed record AlertaResuelta(Guid DesastreId, Guid AgregadoId, long VersionAgregado, DateTimeOffset OcurridoEn,
    Guid AlertaId, TipoAlerta Tipo, bool EnPlan = false) : EventoDominio(DesastreId, AgregadoId, VersionAgregado, OcurridoEn);
