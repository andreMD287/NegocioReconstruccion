using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Aplicacion.Puertos;

/// <summary>Repositorio del agregado PlanReconstruccion. Lo implementa Infraestructura.</summary>
public interface IRepositorioPlanes
{
    Task<PlanReconstruccion?> ObtenerPorDesastreAsync(Guid desastreId, CancellationToken cancelacion = default);

    /// <summary>Registra un plan nuevo; se guarda al confirmar la unidad de trabajo.</summary>
    void Agregar(PlanReconstruccion plan);
}

/// <summary>Repositorio del agregado ProyectoReconstruccion. Lo implementa Infraestructura.</summary>
public interface IRepositorioProyectos
{
    Task<ProyectoReconstruccion?> ObtenerPorIdAsync(Guid proyectoId, CancellationToken cancelacion = default);

    /// <summary>Registra un proyecto nuevo; se guarda al confirmar la unidad de trabajo.</summary>
    void Agregar(ProyectoReconstruccion proyecto);

    /// <summary>Proyectos que debe revisar VigilanciaCronograma (en EnEjecucion).</summary>
    Task<IReadOnlyList<Guid>> ListarIdsEnEjecucionAsync(CancellationToken cancelacion = default);
}

/// <summary>
/// Unidad de trabajo. Confirma en una sola transacción los agregados modificados,
/// sus eventos en el outbox, la auditoría y el registro de la solicitud
/// (secciones 7 y 8.2, RN-RE-16 y RN-RE-18).
/// </summary>
public interface IUnidadDeTrabajo
{
    /// <summary>
    /// Confirma la transacción. Si la versión de algún agregado cambió desde que se cargó,
    /// lanza <see cref="ConflictoDeVersionException"/> y no confirma nada.
    /// </summary>
    Task ConfirmarAsync(CancellationToken cancelacion = default);
}

/// <summary>
/// La lanza la unidad de trabajo cuando otro cambio confirmó antes una versión del agregado.
/// El caso de uso la traduce a <c>CodigoError.ConflictoVersion</c>.
/// </summary>
public sealed class ConflictoDeVersionException(string mensaje, Exception? causa = null)
    : Exception(mensaje, causa);
