using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Planes;

namespace Reconstruccion.Infraestructura.Persistencia.Repositorios;

public sealed class RepositorioPlanesEf(ReconstruccionDbContext db)
    : IRepositorioPlanes
{
    public Task<PlanReconstruccion?> ObtenerPorDesastreAsync(
        Guid desastreId,
        CancellationToken cancelacion = default)
    {
        return db.Planes
            .AsTracking()
            .AsSplitQuery()
            .SingleOrDefaultAsync(
                p => p.DesastreId == desastreId,
                cancelacion);
    }

    public void Agregar(PlanReconstruccion plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        db.Planes.Add(plan);
    }
}