using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Aplicacion.Consultas;

// Consultas de solo lectura (ConsultasService, sección 8.1). No pasan por el
// EjecutorOperaciones: no modifican agregados ni abren transacción. La autorización
// contextual se aplica aquí (RN-RE-19); la lectura la resuelve ILecturaReconstruccion.

/// <summary>Seguimiento consolidado por desastre; rol Autoridad (RF-RE-06).</summary>
public sealed class ConsultarSeguimientoConsolidadoManejador(ILecturaReconstruccion lectura, IUsuarioActual usuario)
    : IManejadorConsulta<ConsultarSeguimientoConsolidado, SeguimientoConsolidadoDto>
{
    public async Task<Resultado<SeguimientoConsolidadoDto>> ManejarAsync(ConsultarSeguimientoConsolidado consulta,
        CancellationToken cancelacion = default)
    {
        if (usuario.Rol != Rol.Autoridad)
        {
            return Resultado<SeguimientoConsolidadoDto>.Fallo(CodigoError.NoAutorizado,
                "El seguimiento consolidado lo consulta la Autoridad.", "RN-RE-19");
        }

        var seguimiento = await lectura.ObtenerSeguimientoAsync(consulta.DesastreId, cancelacion);
        return seguimiento is null
            ? Resultado<SeguimientoConsolidadoDto>.Fallo(CodigoError.NoEncontrado, $"No existe plan para el desastre {consulta.DesastreId}.")
            : Resultado<SeguimientoConsolidadoDto>.Exito(seguimiento);
    }
}

/// <summary>
/// Detalle de un proyecto: Autoridad y Auditor; el Responsable solo sobre los proyectos que
/// formuló o tiene asignados (sección 4).
/// </summary>
public sealed class ConsultarDetalleProyectoManejador(ILecturaReconstruccion lectura, IUsuarioActual usuario)
    : IManejadorConsulta<ConsultarDetalleProyecto, DetalleProyectoDto>
{
    public async Task<Resultado<DetalleProyectoDto>> ManejarAsync(ConsultarDetalleProyecto consulta,
        CancellationToken cancelacion = default)
    {
        switch (usuario.Rol)
        {
            case Rol.Autoridad or Rol.Auditor:
                break;
            case Rol.Responsable:
                var propios = await lectura.ListarProyectosDeUsuarioAsync(usuario.UsuarioId, cancelacion);
                if (!propios.Contains(consulta.ProyectoId))
                {
                    return Resultado<DetalleProyectoDto>.Fallo(CodigoError.NoAutorizado,
                        "El Responsable solo consulta los proyectos que formuló o tiene asignados.", "RN-RE-19");
                }

                break;
            default:
                return Resultado<DetalleProyectoDto>.Fallo(CodigoError.NoAutorizado,
                    "La consulta requiere el rol Autoridad, Auditor o Responsable.", "RN-RE-19");
        }

        var detalle = await lectura.ObtenerDetalleProyectoAsync(consulta.ProyectoId, cancelacion);
        return detalle is null
            ? Resultado<DetalleProyectoDto>.Fallo(CodigoError.NoEncontrado, $"El proyecto {consulta.ProyectoId} no existe.")
            : Resultado<DetalleProyectoDto>.Exito(detalle);
    }
}

/// <summary>
/// Historial de auditoría: Auditor y Autoridad; el Responsable solo sobre sus proyectos
/// (sección 4, numeral 6 del enunciado).
/// </summary>
public sealed class ConsultarHistorialAuditoriaManejador(ILecturaReconstruccion lectura, IUsuarioActual usuario)
    : IManejadorConsulta<ConsultarHistorialAuditoria, Pagina<EntradaAuditoriaDto>>
{
    public const int TamanoMaximoPagina = 200;

    public async Task<Resultado<Pagina<EntradaAuditoriaDto>>> ManejarAsync(ConsultarHistorialAuditoria consulta,
        CancellationToken cancelacion = default)
    {
        if (consulta.Pagina < 1 || consulta.TamanoPagina < 1 || consulta.TamanoPagina > TamanoMaximoPagina)
        {
            return Resultado<Pagina<EntradaAuditoriaDto>>.Fallo(CodigoError.DatosInvalidos,
                $"La página empieza en 1 y el tamaño va de 1 a {TamanoMaximoPagina}.");
        }

        if (consulta.Desde is { } desde && consulta.Hasta is { } hasta && desde > hasta)
        {
            return Resultado<Pagina<EntradaAuditoriaDto>>.Fallo(CodigoError.DatosInvalidos, "El rango de fechas está invertido.");
        }

        IReadOnlyCollection<Guid>? permitidos = null;
        switch (usuario.Rol)
        {
            case Rol.Autoridad or Rol.Auditor:
                break;
            case Rol.Responsable:
                permitidos = await lectura.ListarProyectosDeUsuarioAsync(usuario.UsuarioId, cancelacion);
                if (consulta.ObjetoId is { } objeto && !permitidos.Contains(objeto))
                {
                    return Resultado<Pagina<EntradaAuditoriaDto>>.Fallo(CodigoError.NoAutorizado,
                        "El Responsable solo consulta la auditoría de sus proyectos.", "RN-RE-19");
                }

                break;
            default:
                return Resultado<Pagina<EntradaAuditoriaDto>>.Fallo(CodigoError.NoAutorizado,
                    "La consulta requiere el rol Auditor, Autoridad o Responsable.", "RN-RE-19");
        }

        var pagina = await lectura.ObtenerHistorialAsync(consulta, permitidos, cancelacion);
        return Resultado<Pagina<EntradaAuditoriaDto>>.Exito(pagina);
    }
}
