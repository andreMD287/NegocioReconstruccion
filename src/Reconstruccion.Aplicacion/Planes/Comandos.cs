using Reconstruccion.Aplicacion.Comun;

namespace Reconstruccion.Aplicacion.Planes;

/// <summary>Descarta una necesidad Pendiente, con justificación; rol Autoridad (sección 5.2).</summary>
public sealed record DescartarNecesidad(
    Guid RequestId,
    Guid DesastreId,
    long ExpectedVersionPlan,
    Guid NecesidadId,
    string Justificacion) : IComandoConVersionDePlan;

/// <summary>Reactiva una necesidad Descartada, con justificación; puede reabrir el plan (RN-RE-15).</summary>
public sealed record ReactivarNecesidad(
    Guid RequestId,
    Guid DesastreId,
    long ExpectedVersionPlan,
    Guid NecesidadId,
    string Justificacion) : IComandoConVersionDePlan;

/// <summary>Dónde vive la alerta: las de origen modificado pueden estar en una necesidad del plan.</summary>
public enum UbicacionAlerta
{
    Plan,
    Proyecto
}

/// <summary>
/// La Autoridad resuelve una alerta con justificación (RN-RE-20, RN-RE-21). Si decide
/// suspender, cancelar o descartar, usa además el comando correspondiente.
/// </summary>
/// <param name="AgregadoId">DesastreId si la alerta está en el plan; ProyectoId si está en el proyecto.</param>
/// <param name="ExpectedVersion">Versión esperada del agregado donde está la alerta.</param>
public sealed record ResolverAlerta(
    Guid RequestId,
    UbicacionAlerta Ubicacion,
    Guid AgregadoId,
    long ExpectedVersion,
    Guid AlertaId,
    string Justificacion) : IComandoConVersion;
