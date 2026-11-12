using System.Reflection;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Proyectos;

namespace Reconstruccion.Aplicacion.Tests.Arquitectura;

/// <summary>
/// Aplicación solo depende de Dominio: no conoce Entity Framework, gRPC ni SignalR
/// (sección 2 del diseño).
/// </summary>
public class ReglaDeDependenciasTests
{
    private static readonly Assembly Aplicacion = typeof(Resultado).Assembly;

    [Fact]
    public void Aplicacion_solo_referencia_Dominio_y_la_plataforma()
    {
        var ajenas = Aplicacion.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(nombre => !nombre.StartsWith("System", StringComparison.Ordinal)
                             && nombre != "netstandard"
                             && nombre != "mscorlib"
                             && nombre != "Reconstruccion.Dominio")
            .ToList();

        Assert.Empty(ajenas);
    }

    [Fact]
    public void Todo_comando_lleva_RequestId()
    {
        var comandosSinRequestId = Aplicacion.GetTypes()
            .Where(t => t.Namespace is "Reconstruccion.Aplicacion.Proyectos" or "Reconstruccion.Aplicacion.Planes")
            .Where(t => t.IsClass && !t.IsNested && t.GetMethod("<Clone>$") is not null && t.Name is not ("AporteDto" or "ResponsableDto" or "ContratistaDto" or "HitoDto" or "ReferenciaEvidenciaDto"))
            .Where(t => !typeof(IComando).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        Assert.Empty(comandosSinRequestId);
    }

    [Fact]
    public void Los_comandos_que_modifican_proyecto_y_plan_verifican_las_dos_versiones()
    {
        Type[] coordinados = [typeof(RechazarProyecto), typeof(CancelarProyecto), typeof(FinalizarProyecto)];

        Assert.All(coordinados, t =>
        {
            Assert.True(typeof(IComandoConVersion).IsAssignableFrom(t), $"{t.Name} sin ExpectedVersion");
            Assert.True(typeof(IComandoConVersionDePlan).IsAssignableFrom(t), $"{t.Name} sin ExpectedVersionPlan");
        });
        Assert.True(typeof(IComandoConVersionDePlan).IsAssignableFrom(typeof(CrearProyecto)));
    }
}
