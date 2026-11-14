using Microsoft.Extensions.DependencyInjection.Extensions;
using Reconstruccion.Adaptadores.TiempoReal;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Host.Procesos;
using Reconstruccion.Host.Seguridad;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Lectura;

namespace Reconstruccion.Host;

/// <summary>Composición de la solución (sección 2: Reconstruccion.Host es el punto de entrada).</summary>
public static class Composicion
{
    public static IServiceCollection AgregarReconstruccion(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString("Reconstruccion")
                     ?? Environment.GetEnvironmentVariable("RECONSTRUCCION_DB_CONNECTION")
                     ?? throw new InvalidOperationException(
                         "Falta la cadena de conexión: ConnectionStrings:Reconstruccion o RECONSTRUCCION_DB_CONNECTION.");

        // Persistencia (repositorios, unidad de trabajo, auditoría, solicitudes y outbox) y lectura.
        servicios.AgregarPersistenciaReconstruccion(cadena);
        servicios.AgregarLecturaReconstruccion();

        // Contexto de la operación: usuario del alcance y reloj.
        servicios.AddHttpContextAccessor();
        servicios.AddScoped<UsuarioActualAmbito>();
        servicios.AddScoped<IUsuarioActual>(sp => sp.GetRequiredService<UsuarioActualAmbito>());
        servicios.AddSingleton<IReloj, RelojUtc>();

        // Casos de uso de Aplicación.
        foreach (var (servicio, implementacion) in CatalogoAplicacion.Manejadores)
        {
            servicios.AddScoped(servicio, implementacion);
        }

        foreach (var tipo in CatalogoAplicacion.ServiciosConcretos)
        {
            servicios.AddScoped(tipo);
        }

        // Tiempo real.
        servicios.AddSignalR();
        servicios.AddScoped<INotificadorTiempoReal, NotificadorSignalR>();

        // Cliente hacia Cierre: si Infraestructura no registró uno, queda uno que pide reintento,
        // de modo que los eventos para Cierre esperan en el outbox (sección 5.3).
        servicios.TryAddScoped<IServicioCierre, ServicioCierreNoConfigurado>();

        // gRPC y vigilancia del cronograma.
        servicios.AddGrpc();
        var vigilancia = configuracion.GetSection("Vigilancia").Get<OpcionesVigilancia>() ?? new OpcionesVigilancia();
        servicios.AddSingleton(vigilancia);
        servicios.AddSingleton<VigilanciaCronogramaServicio>();
        servicios.AddHostedService(sp => sp.GetRequiredService<VigilanciaCronogramaServicio>());

        return servicios;
    }
}

/// <summary>
/// Reemplazo mientras no exista el cliente gRPC real hacia Cierre: responde que Cierre no
/// está disponible y el <c>DistribuidorEventos</c> deja el evento pendiente para reintento.
/// </summary>
public sealed class ServicioCierreNoConfigurado : IServicioCierre
{
    public Task<RespuestaCierre> NotificarPlanCompletadoAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default) =>
        throw new InvalidOperationException("El cliente hacia Cierre no está configurado.");

    public Task<RespuestaCierre> NotificarReaperturaAsync(Guid desastreId, long versionPlan, CancellationToken cancelacion = default) =>
        throw new InvalidOperationException("El cliente hacia Cierre no está configurado.");
}
