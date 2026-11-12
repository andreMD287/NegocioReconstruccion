namespace Reconstruccion.Aplicacion.Comun;

/// <summary>
/// Caso de uso que ejecuta un comando. Los adaptadores (gRPC, procesos internos)
/// lo obtienen por inyección de dependencias según el tipo de comando.
/// </summary>
public interface IManejadorComando<in TComando, TRespuesta>
{
    Task<Resultado<TRespuesta>> ManejarAsync(TComando comando, CancellationToken cancelacion = default);
}

/// <summary>Caso de uso de solo lectura.</summary>
public interface IManejadorConsulta<in TConsulta, TRespuesta>
    where TConsulta : IConsulta
{
    Task<Resultado<TRespuesta>> ManejarAsync(TConsulta consulta, CancellationToken cancelacion = default);
}
