using Microsoft.EntityFrameworkCore;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia.Registros;

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

    public DbSet<AuditoriaPersistida> Auditorias =>
        Set<AuditoriaPersistida>();

    public DbSet<SolicitudProcesadaPersistida> SolicitudesProcesadas =>
        Set<SolicitudProcesadaPersistida>();

    public DbSet<EventoOutboxPersistido> EventosOutbox =>
        Set<EventoOutboxPersistido>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(ReconstruccionDbContext).Assembly
        );
    }
}