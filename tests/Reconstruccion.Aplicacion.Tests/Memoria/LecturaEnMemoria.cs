using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Tests.Memoria;

/// <summary>Puerto de lectura sobre el estado en memoria del <see cref="Entorno"/>.</summary>
internal sealed class LecturaEnMemoria(Entorno entorno) : ILecturaReconstruccion
{
    public Task<SeguimientoConsolidadoDto?> ObtenerSeguimientoAsync(Guid desastreId, CancellationToken cancelacion = default)
    {
        var plan = entorno.Planes.FirstOrDefault(p => p.DesastreId == desastreId);
        if (plan is null)
        {
            return Task.FromResult<SeguimientoConsolidadoDto?>(null);
        }

        var proyectos = entorno.Proyectos.Where(p => p.DesastreId == desastreId).ToList();
        var presupuesto = proyectos.Sum(p => p.Presupuesto);
        var avance = presupuesto == 0 ? 0 : proyectos.Sum(p => p.Presupuesto * p.AvanceTotal) / presupuesto;

        return Task.FromResult<SeguimientoConsolidadoDto?>(new SeguimientoConsolidadoDto(desastreId, plan.Estado, plan.Version,
            proyectos.GroupBy(p => p.Estado).ToDictionary(g => g.Key, g => g.Count()),
            presupuesto, proyectos.Sum(p => p.CostoEjecutado), avance,
            plan.Necesidades.GroupBy(n => n.Estado).ToDictionary(g => g.Key, g => g.Count()),
            proyectos.SelectMany(p => p.Alertas).Concat(plan.Alertas).Where(a => a.EstaActiva)
                .GroupBy(a => a.Tipo).ToDictionary(g => g.Key, g => g.Count())));
    }

    public Task<DetalleProyectoDto?> ObtenerDetalleProyectoAsync(Guid proyectoId, CancellationToken cancelacion = default)
    {
        var p = entorno.Proyectos.FirstOrDefault(x => x.Id == proyectoId);
        return Task.FromResult(p is null
            ? null
            : new DetalleProyectoDto(p.Id, p.DesastreId, p.Nombre, p.Descripcion, p.Tipo, p.Estado, p.Version, p.FormuladorId,
                p.ResponsableId, p.Contratista?.Nombre, p.Presupuesto, p.CostoEjecutado, p.AvanceTotal, p.NecesidadIds,
                [], [], [], []));
    }

    public Task<Pagina<EntradaAuditoriaDto>> ObtenerHistorialAsync(ConsultarHistorialAuditoria filtro,
        IReadOnlyCollection<Guid>? proyectosPermitidos, CancellationToken cancelacion = default)
    {
        var entradas = entorno.Auditoria
            .Where(a => filtro.DesastreId is null || a.DesastreId == filtro.DesastreId)
            .Where(a => filtro.ObjetoId is null || a.ObjetoId == filtro.ObjetoId)
            .Where(a => proyectosPermitidos is null || proyectosPermitidos.Contains(a.ObjetoId))
            .Select(a => new EntradaAuditoriaDto(a.Fecha, a.UsuarioId, a.Rol, a.Accion, a.TipoObjeto, a.ObjetoId, a.Resultado, a.Detalle))
            .ToList();

        var pagina = entradas.Skip((filtro.Pagina - 1) * filtro.TamanoPagina).Take(filtro.TamanoPagina).ToList();
        return Task.FromResult(new Pagina<EntradaAuditoriaDto>(pagina, filtro.Pagina, filtro.TamanoPagina, entradas.Count));
    }

    public Task<IReadOnlyList<Guid>> ListarProyectosDeUsuarioAsync(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(entorno.Proyectos
            .Where(p => p.FormuladorId == usuarioId || p.ResponsableId == usuarioId)
            .Select(p => p.Id)
            .ToList());
}
