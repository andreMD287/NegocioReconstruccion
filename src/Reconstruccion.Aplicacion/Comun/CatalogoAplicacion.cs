using Reconstruccion.Aplicacion.Integracion;

namespace Reconstruccion.Aplicacion.Comun;

/// <summary>
/// Servicios de Aplicación que el Host debe registrar con vida scoped. Aplicación no depende
/// del contenedor de inyección de dependencias; el Host recorre esta lista y registra cada par
/// (servicio, implementación).
/// </summary>
public static class CatalogoAplicacion
{
    private static readonly Type[] InterfacesManejador = [typeof(IManejadorComando<,>), typeof(IManejadorConsulta<,>)];

    /// <summary>Manejadores de comandos y consultas, cada uno con la interfaz genérica que implementa.</summary>
    public static IReadOnlyList<(Type Servicio, Type Implementacion)> Manejadores { get; } =
        typeof(CatalogoAplicacion).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && InterfacesManejador.Contains(i.GetGenericTypeDefinition()))
                .Select(i => (Servicio: i, Implementacion: t)))
            .OrderBy(p => p.Implementacion.FullName, StringComparer.Ordinal)
            .ToList();

    /// <summary>Servicios concretos que no implementan una interfaz de manejador.</summary>
    public static IReadOnlyList<Type> ServiciosConcretos { get; } = [typeof(EjecutorOperaciones), typeof(DistribuidorEventos)];
}
