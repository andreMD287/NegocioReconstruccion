using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;

namespace Reconstruccion.Dominio.Tests.Comun;

public class AgregadoRaizTests
{
    [Fact]
    public void Un_plan_nuevo_queda_Abierto_con_version_cero()
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
        Assert.Equal(0, plan.Version);
        Assert.True(plan.TieneVersion(0));
    }

    [Fact]
    public void Un_proyecto_nuevo_queda_Propuesto_con_su_formulador_y_su_evento()
    {
        var proyecto = Fabrica.ProyectoPropuesto(out var formulador);

        Assert.Equal(EstadoProyecto.Propuesto, proyecto.Estado);
        Assert.Equal(formulador, proyecto.FormuladorId);
        Assert.Null(proyecto.ResponsableId);
        Assert.IsType<ProyectoCreado>(Assert.Single(proyecto.Eventos));
    }

    [Fact]
    public void No_se_crea_un_agregado_con_identificador_vacio()
    {
        Assert.Throws<ArgumentException>(() => PlanReconstruccion.Abrir(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void Cada_operacion_incrementa_la_version_una_sola_vez()
    {
        var proyecto = Fabrica.ProyectoPropuesto(out _);
        var antes = proyecto.Version;

        proyecto.EditarPresupuesto(1000m, [new("Nacion", 1000m)]);

        Assert.Equal(antes + 1, proyecto.Version);
    }
}
