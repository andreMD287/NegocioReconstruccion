using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Proyectos;

/// <summary>
/// Agregado ProyectoReconstruccion (sección 3).
/// Contratos v1: solo identidad, estado, formulador y versión; el comportamiento
/// se agrega con los casos de uso.
/// </summary>
public sealed class ProyectoReconstruccion : AgregadoRaiz
{
    private ProyectoReconstruccion()
    {
    }

    private ProyectoReconstruccion(Guid id, Guid desastreId, Guid formuladorId)
        : base(id)
    {
        DesastreId = desastreId;
        FormuladorId = formuladorId;
        Estado = EstadoProyecto.Propuesto;
    }

    public Guid DesastreId { get; private set; }

    /// <summary>Quien creó el proyecto; lo opera mientras está en Propuesto (RN-RE-19).</summary>
    public Guid FormuladorId { get; private set; }

    /// <summary>Responsable asignado; lo opera desde Aprobado (RN-RE-19).</summary>
    public Guid? ResponsableId { get; private set; }

    public EstadoProyecto Estado { get; private set; }

    public static ProyectoReconstruccion Proponer(Guid id, Guid desastreId, Guid formuladorId)
    {
        if (desastreId == Guid.Empty)
        {
            throw new ArgumentException("El proyecto debe pertenecer a un desastre.", nameof(desastreId));
        }

        if (formuladorId == Guid.Empty)
        {
            throw new ArgumentException("El proyecto debe tener un formulador.", nameof(formuladorId));
        }

        return new ProyectoReconstruccion(id, desastreId, formuladorId);
    }
}
