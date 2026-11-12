using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Infraestructura.Persistencia.Repositorios;

public sealed class RepositorioProyectosEf(ReconstruccionDbContext db)
    : IRepositorioProyectos
{
    public Task<ProyectoReconstruccion?> ObtenerPorIdAsync(
        Guid proyectoId,
        CancellationToken cancelacion = default)
    {
        return db.Proyectos
            .AsTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                p => p.Id == proyectoId,
                cancelacion);
    }

    public void Agregar(ProyectoReconstruccion proyecto)
    {
        ArgumentNullException.ThrowIfNull(proyecto);

        db.Proyectos.Add(proyecto);
    }

    public async Task<IReadOnlyList<Guid>> ListarIdsEnEjecucionAsync(
        CancellationToken cancelacion = default)
    {
        return await db.Proyectos
            .Where(p => p.Estado == EstadoProyecto.EnEjecucion)
            .Select(p => p.Id)
            .ToListAsync(cancelacion);
    }
}