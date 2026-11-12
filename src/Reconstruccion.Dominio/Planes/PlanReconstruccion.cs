using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Planes;

/// <summary>
/// Agregado PlanReconstruccion: uno por desastre, contiene sus necesidades (sección 3).
/// Contratos v1: solo identidad, estado y versión; el comportamiento se agrega
/// con los casos de uso.
/// </summary>
public sealed class PlanReconstruccion : AgregadoRaiz
{
    private PlanReconstruccion()
    {
    }

    private PlanReconstruccion(Guid id, Guid desastreId)
        : base(id)
    {
        DesastreId = desastreId;
        Estado = EstadoPlan.Abierto;
    }

    public Guid DesastreId { get; private set; }

    public EstadoPlan Estado { get; private set; }

    public static PlanReconstruccion Abrir(Guid id, Guid desastreId)
    {
        if (desastreId == Guid.Empty)
        {
            throw new ArgumentException("El plan debe pertenecer a un desastre.", nameof(desastreId));
        }

        return new PlanReconstruccion(id, desastreId);
    }
}
