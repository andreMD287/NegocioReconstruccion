using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Infraestructura.Persistencia.Lectura;

/// <summary>
/// Puerto de lectura para las consultas (sección 8.1). Lee la misma base que las operaciones,
/// sin modelo de lectura separado (sección 9.5), y siempre sin seguimiento de cambios.
/// La autorización la aplica el caso de uso antes de llamar a este adaptador.
/// </summary>
public sealed class LecturaReconstruccionEf(ReconstruccionDbContext db) : ILecturaReconstruccion
{
    public async Task<SeguimientoConsolidadoDto?> ObtenerSeguimientoAsync(Guid desastreId, CancellationToken cancelacion = default)
    {
        var plan = await db.Planes.AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(p => p.DesastreId == desastreId, cancelacion);
        if (plan is null)
        {
            return null;
        }

        var proyectos = await db.Proyectos.AsNoTracking().AsSplitQuery()
            .Where(p => p.DesastreId == desastreId)
            .ToListAsync(cancelacion);

        // Proyectos vigentes: los rechazados no comprometen presupuesto.
        var conPresupuesto = proyectos.Where(p => p.Estado != EstadoProyecto.Rechazado).ToList();
        var presupuesto = conPresupuesto.Sum(p => p.Presupuesto);
        var avancePonderado = presupuesto == 0
            ? 0m
            : decimal.Round(conPresupuesto.Sum(p => p.Presupuesto * p.AvanceTotal) / presupuesto, 2);

        var alertasActivas = proyectos.SelectMany(p => p.Alertas)
            .Concat(plan.Alertas)
            .Where(a => a.EstaActiva)
            .GroupBy(a => a.Tipo)
            .ToDictionary(g => g.Key, g => g.Count());

        return new SeguimientoConsolidadoDto(
            desastreId,
            plan.Estado,
            plan.Version,
            proyectos.GroupBy(p => p.Estado).ToDictionary(g => g.Key, g => g.Count()),
            presupuesto,
            conPresupuesto.Sum(p => p.CostoEjecutado),
            avancePonderado,
            plan.Necesidades.GroupBy(n => n.Estado).ToDictionary(g => g.Key, g => g.Count()),
            alertasActivas);
    }

    public async Task<DetalleProyectoDto?> ObtenerDetalleProyectoAsync(Guid proyectoId, CancellationToken cancelacion = default)
    {
        var p = await db.Proyectos.AsNoTracking().AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == proyectoId, cancelacion);
        return p is null ? null : MapearDetalle(p);
    }

    public async Task<Pagina<EntradaAuditoriaDto>> ObtenerHistorialAsync(
        ConsultarHistorialAuditoria filtro,
        IReadOnlyCollection<Guid>? proyectosPermitidos,
        CancellationToken cancelacion = default)
    {
        var consulta = db.Auditorias.AsNoTracking();

        if (filtro.DesastreId is { } desastreId)
        {
            consulta = consulta.Where(a => a.DesastreId == desastreId);
        }

        if (filtro.ObjetoId is { } objetoId)
        {
            consulta = consulta.Where(a => a.ObjetoId == objetoId);
        }

        // PostgreSQL guarda timestamptz en UTC; Npgsql exige offset 0 en los parámetros.
        if (filtro.Desde is { } desde)
        {
            var desdeUtc = desde.ToUniversalTime();
            consulta = consulta.Where(a => a.Fecha >= desdeUtc);
        }

        if (filtro.Hasta is { } hasta)
        {
            var hastaUtc = hasta.ToUniversalTime();
            consulta = consulta.Where(a => a.Fecha <= hastaUtc);
        }

        if (proyectosPermitidos is not null)
        {
            var permitidos = proyectosPermitidos.ToList();
            consulta = consulta.Where(a => permitidos.Contains(a.ObjetoId));
        }

        var total = await consulta.CountAsync(cancelacion);
        var elementos = await consulta
            .OrderByDescending(a => a.Fecha)
            .ThenBy(a => a.Id)
            .Skip((filtro.Pagina - 1) * filtro.TamanoPagina)
            .Take(filtro.TamanoPagina)
            .Select(a => new EntradaAuditoriaDto(a.Fecha, a.UsuarioId, a.Rol, a.Accion, a.TipoObjeto, a.ObjetoId,
                a.Resultado, a.Detalle))
            .ToListAsync(cancelacion);

        return new Pagina<EntradaAuditoriaDto>(elementos, filtro.Pagina, filtro.TamanoPagina, total);
    }

    public async Task<IReadOnlyList<Guid>> ListarProyectosDeUsuarioAsync(Guid usuarioId, CancellationToken cancelacion = default) =>
        await db.Proyectos.AsNoTracking()
            .Where(p => p.FormuladorId == usuarioId || (p.Responsable != null && p.Responsable.UsuarioId == usuarioId))
            .Select(p => p.Id)
            .ToListAsync(cancelacion);

    private static DetalleProyectoDto MapearDetalle(ProyectoReconstruccion p) =>
        new(p.Id, p.DesastreId, p.Nombre, p.Descripcion, p.Tipo, p.Estado, p.Version, p.FormuladorId, p.ResponsableId,
            p.Contratista?.Nombre, p.Presupuesto, p.CostoEjecutado, p.AvanceTotal, p.NecesidadIds.ToList(),
            p.Hitos.OrderBy(h => h.FechaInicio)
                .Select(h => new HitoVistaDto(h.Id, h.Nombre, h.FechaInicio, h.FechaFin, h.Peso, h.Avance)).ToList(),
            p.CambiosPresupuestales.OrderBy(c => c.FechaSolicitud)
                .Select(c => new CambioPresupuestalVistaDto(c.Id, c.Estado, c.PresupuestoAnterior, c.PresupuestoNuevo,
                    c.SolicitanteId, c.ResolutorId, c.FechaSolicitud, c.FechaResolucion, c.Motivo)).ToList(),
            p.Alertas.OrderBy(a => a.FechaGeneracion)
                .Select(a => new AlertaVistaDto(a.Id, a.Tipo, a.Estado, a.FechaGeneracion, a.Descripcion)).ToList(),
            p.Documentos.OrderBy(d => d.FechaRegistro)
                .Select(d => new DocumentoVistaDto(d.Id, d.Tipo, d.Nombre, d.Ubicacion, d.FechaRegistro)).ToList());
}

public static class RegistroLectura
{
    /// <summary>Registra el puerto de lectura de las consultas. Requiere <c>AgregarPersistenciaReconstruccion</c>.</summary>
    public static IServiceCollection AgregarLecturaReconstruccion(this IServiceCollection servicios) =>
        servicios.AddScoped<ILecturaReconstruccion, LecturaReconstruccionEf>();
}
