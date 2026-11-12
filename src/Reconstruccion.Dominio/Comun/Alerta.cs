namespace Reconstruccion.Dominio.Comun;

/// <summary>
/// Alerta de sobrecosto, hito vencido u origen modificado (RN-RE-21).
/// Hay como máximo una alerta activa por tipo y objeto.
/// </summary>
public sealed class Alerta
{
    private Alerta()
    {
        Descripcion = string.Empty;
    }

    internal Alerta(Guid id, TipoAlerta tipo, Guid objetoId, string descripcion, DateTimeOffset fecha)
    {
        Id = id;
        Tipo = tipo;
        ObjetoId = objetoId;
        Descripcion = descripcion;
        FechaGeneracion = fecha;
        Estado = EstadoAlerta.Activa;
    }

    public Guid Id { get; private set; }

    public TipoAlerta Tipo { get; private set; }

    /// <summary>
    /// Objeto al que se refiere: el hito (hito vencido), el proyecto (sobrecosto)
    /// o la necesidad (origen modificado).
    /// </summary>
    public Guid ObjetoId { get; private set; }

    public EstadoAlerta Estado { get; private set; }

    public string Descripcion { get; private set; }

    public DateTimeOffset FechaGeneracion { get; private set; }

    public DateTimeOffset? FechaResolucion { get; private set; }

    public string? Justificacion { get; private set; }

    public bool EstaActiva => Estado == EstadoAlerta.Activa;

    internal void Resolver(DateTimeOffset fecha, string? justificacion)
    {
        Estado = EstadoAlerta.Resuelta;
        FechaResolucion = fecha;
        Justificacion = justificacion;
    }
}
