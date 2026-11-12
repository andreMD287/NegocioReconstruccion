namespace Reconstruccion.Aplicacion.Comun;

/// <summary>
/// Comando enviado por un usuario. Lleva un identificador de solicitud para la
/// idempotencia (RN-RE-18): la misma solicitud devuelve el resultado original.
/// </summary>
public interface IComando
{
    Guid RequestId { get; }
}

/// <summary>
/// Comando que modifica un agregado existente. Lleva la versión esperada para la
/// concurrencia optimista (RN-RE-18).
/// </summary>
public interface IComandoConVersion : IComando
{
    long ExpectedVersion { get; }
}

/// <summary>
/// Comando que además modifica el plan del desastre (CrearProyecto, RechazarProyecto,
/// CancelarProyecto, FinalizarProyecto). Se verifican las dos versiones y un conflicto
/// en cualquiera cancela la operación completa (RN-RE-18).
/// </summary>
public interface IComandoConVersionDePlan : IComando
{
    long ExpectedVersionPlan { get; }
}

/// <summary>Consulta de solo lectura.</summary>
public interface IConsulta
{
}

/// <summary>Respuesta estándar de un comando: identificador y nueva versión del agregado (sección 8.1).</summary>
public sealed record ResultadoComando(Guid Id, long NuevaVersion);

/// <summary>Respuesta de los comandos que modifican proyecto y plan.</summary>
public sealed record ResultadoComandoConPlan(Guid ProyectoId, long NuevaVersion, long NuevaVersionPlan);
