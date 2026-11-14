using Grpc.Core;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Contratos.V1;
using static Reconstruccion.Adaptadores.Grpc.Servicios.TraduccionGrpc;
using Consultas = Reconstruccion.Aplicacion.Consultas;

namespace Reconstruccion.Adaptadores.Grpc.Servicios;

/// <summary>ConsultasService (sección 8.1): vistas de lectura. La autorización la aplica Aplicación.</summary>
public sealed class ConsultasServicioGrpc(
    IManejadorConsulta<ConsultarSeguimientoConsolidado, SeguimientoConsolidadoDto> seguimiento,
    IManejadorConsulta<ConsultarDetalleProyecto, DetalleProyectoDto> detalle,
    IManejadorConsulta<ConsultarHistorialAuditoria, Pagina<EntradaAuditoriaDto>> historial)
    : ConsultasService.ConsultasServiceBase
{
    public override async Task<SeguimientoConsolidadoResponse> ConsultarSeguimientoConsolidado(
        ConsultarSeguimientoConsolidadoRequest r, ServerCallContext c)
    {
        var s = Valor(await seguimiento.ManejarAsync(new ConsultarSeguimientoConsolidado(Id(r.DesastreId, "desastre_id")), c.CancellationToken));

        var respuesta = new SeguimientoConsolidadoResponse
        {
            DesastreId = s.DesastreId.ToString(),
            EstadoPlan = EnumGrpc<EstadoPlanGrpc>(s.EstadoPlan),
            VersionPlan = s.VersionPlan,
            PresupuestoVigenteDecimal = Texto(s.PresupuestoVigente),
            CostoEjecutadoDecimal = Texto(s.CostoEjecutado),
            AvancePonderadoDecimal = Texto(s.AvancePonderado)
        };
        respuesta.ProyectosPorEstado.AddRange(s.ProyectosPorEstado.Select(p => new ConteoPorEstadoProyectoGrpc
            { Estado = EnumGrpc<EstadoProyectoGrpc>(p.Key), Cantidad = p.Value }));
        respuesta.NecesidadesPorEstado.AddRange(s.NecesidadesPorEstado.Select(n => new ConteoPorEstadoNecesidadGrpc
            { Estado = EnumGrpc<EstadoNecesidadGrpc>(n.Key), Cantidad = n.Value }));
        respuesta.AlertasActivas.AddRange(s.AlertasActivas.Select(a => new ConteoPorTipoAlertaGrpc
            { Tipo = EnumGrpc<TipoAlertaGrpc>(a.Key), Cantidad = a.Value }));
        return respuesta;
    }

    public override async Task<DetalleProyectoResponse> ConsultarDetalleProyecto(ConsultarDetalleProyectoRequest r, ServerCallContext c)
    {
        var d = Valor(await detalle.ManejarAsync(new ConsultarDetalleProyecto(Id(r.ProyectoId, "proyecto_id")), c.CancellationToken));

        var respuesta = new DetalleProyectoResponse
        {
            ProyectoId = d.ProyectoId.ToString(),
            DesastreId = d.DesastreId.ToString(),
            Nombre = d.Nombre,
            Descripcion = d.Descripcion,
            Tipo = EnumGrpc<TipoProyectoGrpc>(d.Tipo),
            Estado = EnumGrpc<EstadoProyectoGrpc>(d.Estado),
            Version = d.Version,
            FormuladorId = d.FormuladorId.ToString(),
            PresupuestoVigenteDecimal = Texto(d.PresupuestoVigente),
            CostoEjecutadoDecimal = Texto(d.CostoEjecutado),
            AvanceDecimal = Texto(d.Avance)
        };
        if (d.ResponsableId is { } responsable)
        {
            respuesta.ResponsableId = responsable.ToString();
        }

        if (d.Contratista is { } contratista)
        {
            respuesta.Contratista = contratista;
        }

        respuesta.NecesidadIds.AddRange(d.NecesidadIds.Select(id => id.ToString()));
        respuesta.Hitos.AddRange(d.Hitos.Select(h => new HitoVistaGrpc
        {
            HitoId = h.HitoId.ToString(), Nombre = h.Nombre, FechaInicio = Texto(h.FechaInicio), FechaFin = Texto(h.FechaFin),
            PesoDecimal = Texto(h.Peso), AvanceDecimal = Texto(h.Avance)
        }));
        respuesta.CambiosPresupuestales.AddRange(d.CambiosPresupuestales.Select(Cambio));
        respuesta.Alertas.AddRange(d.Alertas.Select(a => new AlertaVistaGrpc
        {
            AlertaId = a.AlertaId.ToString(), Tipo = EnumGrpc<TipoAlertaGrpc>(a.Tipo), Estado = EnumGrpc<EstadoAlertaGrpc>(a.Estado),
            Fecha = Marca(a.Fecha), Descripcion = a.Descripcion
        }));
        respuesta.Documentos.AddRange(d.Documentos.Select(doc => new DocumentoVistaGrpc
        {
            DocumentoId = doc.DocumentoId.ToString(), Tipo = EnumGrpc<TipoDocumentoGrpc>(doc.Tipo), Nombre = doc.Nombre,
            Ubicacion = doc.Ubicacion, FechaRegistro = Marca(doc.FechaRegistro)
        }));
        return respuesta;
    }

    public override async Task<PaginaAuditoriaResponse> ConsultarHistorialAuditoria(ConsultarHistorialAuditoriaRequest r, ServerCallContext c)
    {
        // Página o tamaño en 0 significan los valores por defecto de Aplicación (1 y 50).
        var consulta = new ConsultarHistorialAuditoria(
            r.HasDesastreId ? Id(r.DesastreId, "desastre_id") : null,
            r.HasObjetoId ? Id(r.ObjetoId, "objeto_id") : null,
            r.Desde?.ToDateTimeOffset(),
            r.Hasta?.ToDateTimeOffset(),
            r.Pagina == 0 ? 1 : r.Pagina,
            r.TamanoPagina == 0 ? 50 : r.TamanoPagina);

        var pagina = Valor(await historial.ManejarAsync(consulta, c.CancellationToken));

        var respuesta = new PaginaAuditoriaResponse
        {
            NumeroPagina = pagina.NumeroPagina, TamanoPagina = pagina.TamanoPagina, Total = pagina.Total
        };
        respuesta.Elementos.AddRange(pagina.Elementos.Select(e =>
        {
            var entrada = new EntradaAuditoriaGrpc
            {
                Fecha = Marca(e.Fecha), UsuarioId = e.UsuarioId.ToString(), Rol = EnumGrpc<RolGrpc>(e.Rol), Accion = e.Accion,
                TipoObjeto = e.TipoObjeto, ObjetoId = e.ObjetoId.ToString(), Resultado = EnumGrpc<ResultadoOperacionGrpc>(e.Resultado)
            };
            if (e.Detalle is { } detalleEntrada)
            {
                entrada.Detalle = detalleEntrada;
            }

            return entrada;
        }));
        return respuesta;
    }

    private static CambioPresupuestalVistaGrpc Cambio(CambioPresupuestalVistaDto c)
    {
        var vista = new CambioPresupuestalVistaGrpc
        {
            CambioId = c.CambioId.ToString(), Estado = EnumGrpc<EstadoCambioPresupuestalGrpc>(c.Estado),
            PresupuestoAnteriorDecimal = Texto(c.PresupuestoAnterior), PresupuestoNuevoDecimal = Texto(c.PresupuestoNuevo),
            SolicitanteId = c.SolicitanteId.ToString(), FechaSolicitud = Marca(c.FechaSolicitud), Motivo = c.Motivo
        };
        if (c.ResolutorId is { } resolutor)
        {
            vista.ResolutorId = resolutor.ToString();
        }

        if (c.FechaResolucion is { } fecha)
        {
            vista.FechaResolucion = Marca(fecha);
        }

        return vista;
    }
}
