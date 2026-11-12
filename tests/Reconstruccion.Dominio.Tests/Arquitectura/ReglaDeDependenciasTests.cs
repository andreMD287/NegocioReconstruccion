using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Tests.Arquitectura;

/// <summary>
/// Dominio no depende de ningún framework, ORM ni tecnología de comunicación
/// (secciones 1.2 y 2 del diseño).
/// </summary>
public class ReglaDeDependenciasTests
{
    [Fact]
    public void Dominio_solo_referencia_ensamblados_de_la_plataforma()
    {
        var referencias = typeof(AgregadoRaiz).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToList();

        var ajenas = referencias
            .Where(nombre => !nombre.StartsWith("System", StringComparison.Ordinal)
                             && nombre != "netstandard"
                             && nombre != "mscorlib")
            .ToList();

        Assert.Empty(ajenas);
    }
}
