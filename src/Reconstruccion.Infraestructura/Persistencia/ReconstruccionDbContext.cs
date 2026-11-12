using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Infraestructura.Persistencia;

public sealed class ReconstruccionDbContext : DbContext
{
    public ReconstruccionDbContext(
        DbContextOptions<ReconstruccionDbContext> options)
        : base(options)
    {
    }

    public DbSet<PlanReconstruccion> Planes =>
        Set<PlanReconstruccion>();

    public DbSet<ProyectoReconstruccion> Proyectos =>
        Set<ProyectoReconstruccion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReconstruccionDbContext).Assembly
        );
    }
}