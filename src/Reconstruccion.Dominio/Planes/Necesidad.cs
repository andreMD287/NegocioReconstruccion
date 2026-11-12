using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Planes;

/// <summary>Ubicación del activo evaluado.</summary>
public sealed record Ubicacion(double Latitud, double Longitud, string? Descripcion);

/// <summary>
/// Copia de referencia de la evaluación de origen (RN-RE-04). La autoridad sobre la
/// evaluación sigue en Caracterización. El nivel de daño se guarda como texto porque
/// las categorías son configurables (RN-RE-02).
/// </summary>
public sealed record EvaluacionOrigen(
    Guid EvaluacionId,
    long VersionEvaluacion,
    Guid ActivoId,
    string TipoActivo,
    string NivelDano,
    DateTimeOffset FechaValidacion,
    Ubicacion Ubicacion);

/// <summary>
/// Activo evaluado que requiere intervención. Pertenece como máximo a un proyecto
/// no terminal (RN-RE-03).
/// </summary>
public sealed class Necesidad
{
    private Necesidad()
    {
        Origen = null!;
    }

    internal Necesidad(Guid id, EvaluacionOrigen origen, int cicloRecepcion)
    {
        Id = id;
        Origen = origen;
        CicloRecepcion = cicloRecepcion;
        Estado = EstadoNecesidad.Pendiente;
    }

    public Guid Id { get; private set; }

    public EvaluacionOrigen Origen { get; private set; }

    /// <summary>Último ciclo de caracterización en que llegó la evaluación (RN-RE-22).</summary>
    public int CicloRecepcion { get; private set; }

    public EstadoNecesidad Estado { get; private set; }

    /// <summary>Proyecto no terminal al que está asignada, si lo está.</summary>
    public Guid? ProyectoId { get; private set; }

    /// <summary>La evaluación de origen fue revocada por Caracterización (RN-RE-20).</summary>
    public bool EvaluacionRevocada { get; private set; }

    public string? UltimaJustificacion { get; private set; }

    internal void MarcarRecibidaEnCiclo(int ciclo) => CicloRecepcion = Math.Max(CicloRecepcion, ciclo);

    internal void ActualizarOrigen(EvaluacionOrigen origen) => Origen = origen;

    internal void MarcarRevocada(long versionEvaluacion)
    {
        EvaluacionRevocada = true;
        Origen = Origen with { VersionEvaluacion = versionEvaluacion };
    }

    internal void Asignar(Guid proyectoId)
    {
        Estado = EstadoNecesidad.Asignada;
        ProyectoId = proyectoId;
    }

    internal void Liberar()
    {
        Estado = EstadoNecesidad.Pendiente;
        ProyectoId = null;
    }

    internal void Atender() => Estado = EstadoNecesidad.Atendida;

    internal void Descartar(string justificacion)
    {
        Estado = EstadoNecesidad.Descartada;
        UltimaJustificacion = justificacion;
    }

    internal void Reactivar(string justificacion)
    {
        Estado = EstadoNecesidad.Pendiente;
        UltimaJustificacion = justificacion;
    }
}
