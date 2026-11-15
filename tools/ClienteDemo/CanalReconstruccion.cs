using Grpc.Net.Client;
using Reconstruccion.Contratos.V1;

namespace Reconstruccion.ClienteDemo;

/// <summary>
/// Canal gRPC hacia el Host, con los tres servicios de entrada de la sección 8.1. Acepta el
/// certificado de desarrollo de ASP.NET Core sin validarlo: es un cliente de demostración
/// contra un Host local, no un cliente de producción.
/// </summary>
internal sealed class CanalReconstruccion : IAsyncDisposable
{
    public ProyectosService.ProyectosServiceClient Proyectos { get; }

    public CaracterizacionService.CaracterizacionServiceClient Caracterizacion { get; }

    public ConsultasService.ConsultasServiceClient Consultas { get; }

    private readonly GrpcChannel _canal;

    public CanalReconstruccion(Uri direccion)
    {
        var manejador = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _canal = GrpcChannel.ForAddress(direccion, new GrpcChannelOptions { HttpHandler = manejador });
        Proyectos = new ProyectosService.ProyectosServiceClient(_canal);
        Caracterizacion = new CaracterizacionService.CaracterizacionServiceClient(_canal);
        Consultas = new ConsultasService.ConsultasServiceClient(_canal);
    }

    public ValueTask DisposeAsync()
    {
        _canal.Dispose();
        return ValueTask.CompletedTask;
    }
}
