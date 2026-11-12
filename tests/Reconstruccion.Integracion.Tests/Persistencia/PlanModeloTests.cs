using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Infraestructura.Persistencia.Configuraciones;
using Xunit;

namespace Reconstruccion.Integracion.Tests.Persistencia;

public sealed class PlanModeloTests
{
    [Fact]
    public void ModeloPlan_ConfiguraAgregadoYComponentes()
    {
        var opciones = new DbContextOptionsBuilder<ContextoSoloPlan>()
            .UseNpgsql("Host=localhost;Database=solo_modelo")
            .Options;

        using var contexto = new ContextoSoloPlan(opciones);

        // Acceder a Model fuerza a EF Core a construir
        // y validar el modelo, sin conectarse a PostgreSQL.
        var modelo = contexto.Model;

        var plan = modelo.FindEntityType(typeof(PlanReconstruccion));

        Assert.NotNull(plan);

        // RN-RE-18: Version es token de concurrencia.
        var version = plan.FindProperty("Version");

        Assert.NotNull(version);
        Assert.True(version.IsConcurrencyToken);

        // La propiedad de eventos no se almacena como entidad.
        Assert.Null(plan.FindNavigation("Eventos"));

        // Las listas del plan son navegaciones.
        Assert.NotNull(plan.FindNavigation("Necesidades"));
        Assert.NotNull(plan.FindNavigation("Alertas"));

        // Las entidades dependientes pertenecen al agregado.
        Assert.Contains(
            modelo.GetEntityTypes(),
            entidad => entidad.ClrType == typeof(Necesidad)
                       && entidad.IsOwned()
        );

        Assert.Contains(
            modelo.GetEntityTypes(),
            entidad => entidad.ClrType == typeof(Alerta)
                       && entidad.IsOwned()
        );

        // La copia de evaluacion es un tipo owned.
        Assert.Contains(
            modelo.GetEntityTypes(),
            entidad => entidad.ClrType == typeof(EvaluacionOrigen)
                       && entidad.IsOwned()
        );
    }

    private sealed class ContextoSoloPlan : DbContext
    {
        public ContextoSoloPlan(
            DbContextOptions<ContextoSoloPlan> options)
            : base(options)
        {
        }

        public DbSet<PlanReconstruccion> Planes =>
            Set<PlanReconstruccion>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(
                new PlanReconstruccionConfiguration()
            );
        }
    }
}