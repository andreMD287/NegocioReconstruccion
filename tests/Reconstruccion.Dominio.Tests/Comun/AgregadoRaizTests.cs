using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Dominio.Comun;

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
    public void Un_proyecto_nuevo_queda_Propuesto_con_su_formulador()
    {
        var formulador = Guid.NewGuid();

        var proyecto = ProyectoReconstruccion.Proponer(Guid.NewGuid(), Guid.NewGuid(), formulador);

        Assert.Equal(EstadoProyecto.Propuesto, proyecto.Estado);
        Assert.Equal(formulador, proyecto.FormuladorId);
        Assert.Null(proyecto.ResponsableId);
    }

    [Fact]
    public void No_se_crea_un_agregado_con_identificador_vacio()
    {
        Assert.Throws<ArgumentException>(() => PlanReconstruccion.Abrir(Guid.Empty, Guid.NewGuid()));
    }

    [Fact]
    public void No_se_crea_un_proyecto_sin_formulador()
    {
        Assert.Throws<ArgumentException>(() => ProyectoReconstruccion.Proponer(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
    }
}
