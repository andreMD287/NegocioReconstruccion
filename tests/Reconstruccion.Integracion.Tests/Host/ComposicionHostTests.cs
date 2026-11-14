using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Reconstruccion.Adaptadores.Grpc.Servicios;
using Reconstruccion.Adaptadores.TiempoReal;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Host;
using Reconstruccion.Host.Seguridad;

namespace Reconstruccion.Integracion.Tests.Host;

/// <summary>
/// Composición del Host sin base de datos: todos los servicios se resuelven y el hub aplica la
/// autorización de grupos de la sección 8.2.
/// </summary>
public class ComposicionHostTests
{
    [Fact]
    public void El_Host_resuelve_todos_los_casos_de_uso_servicios_gRPC_y_procesos()
    {
        using var proveedor = Construir();
        using var alcance = proveedor.CreateScope();
        alcance.ServiceProvider.GetRequiredService<UsuarioActualAmbito>().ComoSistema();

        foreach (var (servicio, _) in CatalogoAplicacion.Manejadores)
        {
            Assert.NotNull(alcance.ServiceProvider.GetRequiredService(servicio));
        }

        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<DistribuidorEventos>());
        Assert.IsType<NotificadorSignalR>(alcance.ServiceProvider.GetRequiredService<INotificadorTiempoReal>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<ILecturaReconstruccion>());
        Assert.NotNull(ActivatorUtilities.CreateInstance<ProyectosServicioGrpc>(alcance.ServiceProvider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<CaracterizacionServicioGrpc>(alcance.ServiceProvider));
        Assert.NotNull(ActivatorUtilities.CreateInstance<ConsultasServicioGrpc>(alcance.ServiceProvider));
    }

    [Fact]
    public void El_usuario_actual_es_el_mismo_en_todo_el_alcance()
    {
        using var proveedor = Construir();
        using var alcance = proveedor.CreateScope();

        Assert.Same(alcance.ServiceProvider.GetRequiredService<UsuarioActualAmbito>(),
            alcance.ServiceProvider.GetRequiredService<IUsuarioActual>());
    }

    [Fact]
    public async Task Sin_cliente_de_Cierre_los_eventos_de_Cierre_quedan_para_reintento()
    {
        using var proveedor = Construir();
        using var alcance = proveedor.CreateScope();
        var cierre = alcance.ServiceProvider.GetRequiredService<IServicioCierre>();

        Assert.IsType<ServicioCierreNoConfigurado>(cierre);
        await Assert.ThrowsAsync<InvalidOperationException>(() => cierre.NotificarPlanCompletadoAsync(Guid.NewGuid(), 1));
    }

    // --- Hub: autorización de grupos (sección 8.2) -------------------------------------

    [Fact]
    public async Task La_Autoridad_se_une_a_los_grupos_del_desastre()
    {
        var (hub, grupos) = Hub(Rol.Autoridad, propios: []);
        var desastre = Guid.NewGuid();

        await hub.UnirseADesastre(desastre);

        Assert.Equal([$"desastre-{desastre}"], grupos.Unidos);
    }

    [Fact]
    public async Task El_Responsable_no_se_une_a_los_grupos_del_desastre()
    {
        var (hub, grupos) = Hub(Rol.Responsable, propios: []);

        await Assert.ThrowsAsync<HubException>(() => hub.UnirseADesastre(Guid.NewGuid()));
        Assert.Empty(grupos.Unidos);
    }

    [Fact]
    public async Task El_Responsable_solo_se_une_a_sus_proyectos()
    {
        var propio = Guid.NewGuid();
        var (hub, grupos) = Hub(Rol.Responsable, propios: [propio]);

        await hub.UnirseAProyecto(propio);
        await Assert.ThrowsAsync<HubException>(() => hub.UnirseAProyecto(Guid.NewGuid()));

        Assert.Equal([$"proyecto-{propio}"], grupos.Unidos);
    }

    // --- Ayudas ---------------------------------------------------------------------------

    /// <summary>Construye el Host real (sin arrancarlo) y valida todas las dependencias al construir.</summary>
    private static Aplicacion Construir()
    {
        var builder = Microsoft.AspNetCore.Builder.WebApplication.CreateBuilder(
            new Microsoft.AspNetCore.Builder.WebApplicationOptions { EnvironmentName = "Testing" });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Reconstruccion"] = "Host=localhost;Database=no_se_usa;Username=x;Password=x",
            ["Vigilancia:Habilitada"] = "false",
            ["Jwt:Clave"] = "clave-de-prueba-con-mas-de-treinta-y-dos-caracteres"
        });
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateOnBuild = true;
            o.ValidateScopes = true;
        });
        builder.Services.AgregarAutenticacionJwt(builder.Configuration);
        builder.Services.AgregarReconstruccion(builder.Configuration);
        return new Aplicacion(builder.Build());
    }

    private sealed class Aplicacion(Microsoft.AspNetCore.Builder.WebApplication app) : IDisposable
    {
        public IServiceScope CreateScope() => app.Services.CreateScope();

        public void Dispose() => ((IDisposable)app).Dispose();
    }

    private static (ReconstruccionHub Hub, GruposFalsos Grupos) Hub(Rol rol, IReadOnlyList<Guid> propios)
    {
        var grupos = new GruposFalsos();
        var hub = new ReconstruccionHub(new UsuarioFijo(rol), new LecturaFija(propios))
        {
            Groups = grupos,
            Context = new ContextoFalso()
        };
        return (hub, grupos);
    }


    private sealed class UsuarioFijo(Rol rol) : IUsuarioActual
    {
        public Guid UsuarioId { get; } = Guid.NewGuid();
        public Rol Rol { get; } = rol;
        public Guid? OrganismoId => null;
    }

    private sealed class LecturaFija(IReadOnlyList<Guid> propios) : ILecturaReconstruccion
    {
        public Task<IReadOnlyList<Guid>> ListarProyectosDeUsuarioAsync(Guid usuarioId, CancellationToken cancelacion = default) =>
            Task.FromResult(propios);

        public Task<SeguimientoConsolidadoDto?> ObtenerSeguimientoAsync(Guid desastreId, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public Task<DetalleProyectoDto?> ObtenerDetalleProyectoAsync(Guid proyectoId, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();

        public Task<Pagina<EntradaAuditoriaDto>> ObtenerHistorialAsync(ConsultarHistorialAuditoria filtro,
            IReadOnlyCollection<Guid>? proyectosPermitidos, CancellationToken cancelacion = default) =>
            throw new NotSupportedException();
    }

    private sealed class GruposFalsos : IGroupManager
    {
        public List<string> Unidos { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Unidos.Add(groupName);
            return Task.CompletedTask;
        }

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Unidos.Remove(groupName);
            return Task.CompletedTask;
        }
    }

    private sealed class ContextoFalso : HubCallerContext
    {
        public override string ConnectionId => "conexion-de-prueba";
        public override string? UserIdentifier => null;
        public override System.Security.Claims.ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override Microsoft.AspNetCore.Http.Features.IFeatureCollection Features { get; } = new Microsoft.AspNetCore.Http.Features.FeatureCollection();
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }
}
