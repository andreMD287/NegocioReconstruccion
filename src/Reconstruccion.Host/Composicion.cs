using Microsoft.Extensions.DependencyInjection.Extensions;
using Reconstruccion.Adaptadores.Grpc.Clientes;
using Reconstruccion.Adaptadores.TiempoReal;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Host.Procesos;
using Reconstruccion.Host.Seguridad;
using Reconstruccion.Infraestructura.Persistencia;
using Reconstruccion.Infraestructura.Persistencia.Lectura;
using Reconstruccion.Infraestructura.Persistencia.Publicacion;

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

        // Cliente gRPC hacia Cierre con timeouts, reintentos y circuit breaker. Sin dirección
        // configurada queda el provisional, que pide reintento: los eventos para Cierre esperan
        // en el outbox (sección 5.3).
        var direccionCierre = configuracion["Cierre:Direccion"];
        if (!string.IsNullOrWhiteSpace(direccionCierre))
        {
            servicios.AgregarClienteCierreGrpc(direccionCierre);
        }

        servicios.TryAddScoped<IServicioCierre, ServicioCierreNoConfigurado>();

        // Publicador del outbox: entrega los eventos confirmados a SignalR y a Cierre, en orden
        // por desastre, con la identidad Sistema (sección 8.2).
        var segundosPublicador = configuracion.GetValue("Publicador:IntervaloSegundos", 5);
        servicios.AgregarPublicadorOutbox(
            sp => sp.GetRequiredService<UsuarioActualAmbito>().ComoSistema(),
            TimeSpan.FromSeconds(Math.Max(1, segundosPublicador)));

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
