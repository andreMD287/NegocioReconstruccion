using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class ProyectoModeloTests
{
    [Fact]
    public void ModeloProyecto_ConfiguraAgregadoYColecciones()
    {
        var opciones = new DbContextOptionsBuilder<ReconstruccionDbContext>()
            .UseNpgsql("Host=localhost;Database=solo_modelo")
            .Options;

        using var contexto = new ReconstruccionDbContext(opciones);

        var modelo = contexto.Model;
        var proyecto = modelo.FindEntityType(typeof(ProyectoReconstruccion));

        Assert.NotNull(proyecto);

        var version = proyecto.FindProperty("Version");
        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);

        var necesidades = proyecto.FindProperty("NecesidadIds");
        Assert.NotNull(necesidades);
        Assert.Equal("_necesidadIds", necesidades.FieldInfo?.Name);

        Assert.NotNull(proyecto.FindNavigation("Aportes"));
        Assert.NotNull(proyecto.FindNavigation("Hitos"));
        Assert.NotNull(proyecto.FindNavigation("Avances"));
        Assert.NotNull(proyecto.FindNavigation("Costos"));
        Assert.NotNull(proyecto.FindNavigation("Documentos"));
        Assert.NotNull(proyecto.FindNavigation("CambiosPresupuestales"));
        Assert.NotNull(proyecto.FindNavigation("Alertas"));

        Assert.Null(proyecto.FindProperty("CostoEjecutado"));
        Assert.Null(proyecto.FindProperty("AvanceTotal"));
        Assert.Null(proyecto.FindNavigation("CambioSolicitado"));
    }
}