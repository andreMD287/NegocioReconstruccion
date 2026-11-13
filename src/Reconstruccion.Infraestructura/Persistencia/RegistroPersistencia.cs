using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;
using Reconstruccion.Infraestructura.Persistencia.Repositorios;

namespace Reconstruccion.Infraestructura.Persistencia;

public static class RegistroPersistencia
{
    public static IServiceCollection AgregarPersistenciaReconstruccion(
        this IServiceCollection servicios,
        string cadenaConexion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentException.ThrowIfNullOrWhiteSpace(cadenaConexion);

        // EF Core registra tambien ReconstruccionDbContext como Scoped.
        // La fabrica permite crear contextos independientes para rechazos.
        servicios.AddDbContextFactory<ReconstruccionDbContext>(
            opciones => opciones.UseNpgsql(cadenaConexion),
            ServiceLifetime.Scoped);

        servicios.AddScoped<IRepositorioPlanes, RepositorioPlanesEf>();
        servicios.AddScoped<IRepositorioProyectos, RepositorioProyectosEf>();

        servicios.AddScoped<IRegistroAuditoria, RegistroAuditoriaEf>();
        servicios.AddScoped<IRegistroSolicitudes, RegistroSolicitudesEf>();

        servicios.AddScoped<IOutbox, OutboxEf>();
        servicios.AddScoped<IUnidadDeTrabajo, UnidadDeTrabajoEf>();

        return servicios;
    }
}