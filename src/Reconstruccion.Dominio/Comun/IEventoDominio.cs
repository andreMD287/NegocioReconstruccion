namespace Reconstruccion.Dominio.Comun;

/// <summary>
/// Evento de dominio. Se guarda en el outbox en la misma transacción que el cambio
/// y se entrega al menos una vez (sección 8.2 del diseño).
/// </summary>
public interface IEventoDominio
{
    Guid EventoId { get; }

    DateTimeOffset OcurridoEn { get; }

    Guid DesastreId { get; }

    /// <summary>Plan o proyecto que emitió el evento.</summary>
    Guid AgregadoId { get; }

    /// <summary>Versión del agregado después del cambio.</summary>
    long VersionAgregado { get; }
}

/// <summary>Base para los eventos concretos.</summary>
public abstract record EventoDominio(
    Guid DesastreId,
    Guid AgregadoId,
    long VersionAgregado,
    DateTimeOffset OcurridoEn) : IEventoDominio
{
    public Guid EventoId { get; init; } = Guid.NewGuid();
}
