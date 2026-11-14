using Grpc.Core;
using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Planes;
using Reconstruccion.Aplicacion.Proyectos;
using Reconstruccion.Contratos.V1;
using Reconstruccion.Dominio.Comun;
using static Reconstruccion.Adaptadores.Grpc.Servicios.TraduccionGrpc;
using AplicacionPlanes = Reconstruccion.Aplicacion.Planes;
using AplicacionProyectos = Reconstruccion.Aplicacion.Proyectos;

namespace Reconstruccion.Adaptadores.Grpc.Servicios;

/// <summary>
/// ProyectosService (sección 8.1): un método por cada comando de la sección 4. Traduce el
/// mensaje al comando de Aplicación, ejecuta el caso de uso y traduce la respuesta o el error.
/// </summary>
public sealed class ProyectosServicioGrpc(IServiceProvider servicios) : ProyectosService.ProyectosServiceBase
{
    // --- Ciclo de vida ----------------------------------------------------------

    public override async Task<ResultadoComandoConPlanResponse> CrearProyecto(CrearProyectoRequest r, ServerCallContext c) =>
        ConPlan(await Ejecutar<AplicacionProyectos.CrearProyecto, ResultadoComandoConPlan>(new(
            Id(r.RequestId, "request_id"), Id(r.DesastreId, "desastre_id"), r.ExpectedVersionPlan, r.Nombre, r.Descripcion,
            Enum<TipoProyecto>(r.Tipo, "tipo"), Ids(r.NecesidadIds, "necesidad_ids")), c));

    public override async Task<ResultadoComandoResponse> EditarPresupuestoPropuesto(EditarPresupuestoPropuestoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.EditarPresupuestoPropuesto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion,
            Decimal(r.PresupuestoDecimal, "presupuesto_decimal"), Aportes(r.Aportes)), c));

    public override async Task<ResultadoComandoResponse> AprobarProyecto(AprobarProyectoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.AprobarProyecto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion), c));

    public override async Task<ResultadoComandoConPlanResponse> RechazarProyecto(RechazarProyectoRequest r, ServerCallContext c) =>
        ConPlan(await Ejecutar<AplicacionProyectos.RechazarProyecto, ResultadoComandoConPlan>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, r.ExpectedVersionPlan, r.Motivo), c));

    public override async Task<ResultadoComandoResponse> IniciarProyecto(IniciarProyectoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.IniciarProyecto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion), c));

    public override async Task<ResultadoComandoResponse> SuspenderProyecto(SuspenderProyectoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.SuspenderProyecto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, r.Motivo), c));

    public override async Task<ResultadoComandoResponse> ReanudarProyecto(ReanudarProyectoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.ReanudarProyecto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion), c));

    public override async Task<ResultadoComandoConPlanResponse> CancelarProyecto(CancelarProyectoRequest r, ServerCallContext c) =>
        ConPlan(await Ejecutar<AplicacionProyectos.CancelarProyecto, ResultadoComandoConPlan>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, r.ExpectedVersionPlan, r.Motivo), c));

    public override async Task<ResultadoComandoConPlanResponse> FinalizarProyecto(FinalizarProyectoRequest r, ServerCallContext c) =>
        ConPlan(await Ejecutar<AplicacionProyectos.FinalizarProyecto, ResultadoComandoConPlan>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, r.ExpectedVersionPlan), c));

    // --- Gestión operativa --------------------------------------------------------

    public override async Task<ResultadoComandoResponse> AsignarParticipantes(AsignarParticipantesRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.AsignarParticipantes, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion,
            r.Responsable is null ? null : new ResponsableDto(Id(r.Responsable.UsuarioId, "responsable.usuario_id"),
                Id(r.Responsable.OrganismoId, "responsable.organismo_id")),
            r.Contratista is null ? null : new ContratistaDto(r.Contratista.Identificacion, r.Contratista.Nombre)), c));

    public override async Task<ResultadoComandoResponse> DefinirCronograma(DefinirCronogramaRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.DefinirCronograma, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion,
            Fecha(r.FechaInicio, "fecha_inicio"), Fecha(r.FechaFin, "fecha_fin"),
            r.Hitos.Select(h => new HitoDto(h.Nombre, Fecha(h.FechaInicio, "hitos.fecha_inicio"),
                Fecha(h.FechaFin, "hitos.fecha_fin"), Decimal(h.PesoDecimal, "hitos.peso_decimal"))).ToList()), c));

    public override async Task<ResultadoComandoResponse> ReprogramarHito(ReprogramarHitoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.ReprogramarHito, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, Id(r.HitoId, "hito_id"),
            Fecha(r.NuevaFechaInicio, "nueva_fecha_inicio"), Fecha(r.NuevaFechaFin, "nueva_fecha_fin"), r.Motivo), c));

    public override async Task<ResultadoComandoResponse> RegistrarAvance(RegistrarAvanceRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.RegistrarAvance, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, Id(r.HitoId, "hito_id"),
            Decimal(r.PorcentajeDecimal, "porcentaje_decimal"), Fecha(r.FechaEfectiva, "fecha_efectiva"),
            r.Evidencias.Select(e => new ReferenciaEvidenciaDto(e.Identificador, e.Tipo, e.Ubicacion)).ToList(),
            r.HasMotivoCorreccion ? r.MotivoCorreccion : null), c));

    public override async Task<ResultadoComandoResponse> RegistrarCosto(RegistrarCostoRequest r, ServerCallContext c)
    {
        var revertido = r.HasCostoRevertidoId ? Id(r.CostoRevertidoId, "costo_revertido_id") : (Guid?)null;
        // En un reverso el monto lo toma el dominio del costo original (RN-RE-11).
        var monto = revertido is null || !string.IsNullOrWhiteSpace(r.MontoDecimal) ? Decimal(r.MontoDecimal, "monto_decimal") : 0m;
        return Simple(await Ejecutar<AplicacionProyectos.RegistrarCosto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, monto, r.Concepto,
            Fecha(r.FechaEfectiva, "fecha_efectiva"), revertido, r.HasMotivoReverso ? r.MotivoReverso : null), c));
    }

    public override async Task<ResultadoComandoResponse> AdjuntarDocumento(AdjuntarDocumentoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.AdjuntarDocumento, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion,
            Enum<TipoDocumento>(r.Tipo, "tipo"), r.Nombre, r.Ubicacion), c));

    // --- Presupuesto ----------------------------------------------------------------

    public override async Task<ResultadoComandoResponse> SolicitarCambioPresupuesto(SolicitarCambioPresupuestoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.SolicitarCambioPresupuesto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion,
            Decimal(r.NuevoPresupuestoDecimal, "nuevo_presupuesto_decimal"), Aportes(r.NuevosAportes), r.Motivo), c));

    public override async Task<ResultadoComandoResponse> ResolverCambioPresupuesto(ResolverCambioPresupuestoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.ResolverCambioPresupuesto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, Id(r.CambioId, "cambio_id"),
            r.Aprobar, r.HasMotivo ? r.Motivo : null), c));

    public override async Task<ResultadoComandoResponse> RetirarCambioPresupuesto(RetirarCambioPresupuestoRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionProyectos.RetirarCambioPresupuesto, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.ProyectoId, "proyecto_id"), r.ExpectedVersion, Id(r.CambioId, "cambio_id"), r.Motivo), c));

    // --- Necesidades y alertas ----------------------------------------------------------

    public override async Task<ResultadoComandoResponse> DescartarNecesidad(DescartarNecesidadRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionPlanes.DescartarNecesidad, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.DesastreId, "desastre_id"), r.ExpectedVersionPlan,
            Id(r.NecesidadId, "necesidad_id"), r.Justificacion), c));

    public override async Task<ResultadoComandoResponse> ReactivarNecesidad(ReactivarNecesidadRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionPlanes.ReactivarNecesidad, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Id(r.DesastreId, "desastre_id"), r.ExpectedVersionPlan,
            Id(r.NecesidadId, "necesidad_id"), r.Justificacion), c));

    public override async Task<ResultadoComandoResponse> ResolverAlerta(ResolverAlertaRequest r, ServerCallContext c) =>
        Simple(await Ejecutar<AplicacionPlanes.ResolverAlerta, ResultadoComando>(new(
            Id(r.RequestId, "request_id"), Enum<UbicacionAlerta>(r.Ubicacion, "ubicacion"), Id(r.AgregadoId, "agregado_id"),
            r.ExpectedVersion, Id(r.AlertaId, "alerta_id"), r.Justificacion), c));

    // --- Ayudas ----------------------------------------------------------------------------

    private async Task<TRespuesta> Ejecutar<TComando, TRespuesta>(TComando comando, ServerCallContext contexto)
    {
        var manejador = (IManejadorComando<TComando, TRespuesta>?)servicios.GetService(typeof(IManejadorComando<TComando, TRespuesta>))
            ?? throw new RpcException(new Status(StatusCode.Unimplemented, $"No hay caso de uso para {typeof(TComando).Name}."));
        return Valor(await manejador.ManejarAsync(comando, contexto.CancellationToken));
    }

    private static List<AporteDto> Aportes(IEnumerable<AporteDtoGrpc> aportes) =>
        aportes.Select(a => new AporteDto(a.Fuente, Decimal(a.MontoDecimal, "aportes.monto_decimal"))).ToList();

    private static ResultadoComandoResponse Simple(ResultadoComando r) =>
        new() { Id = r.Id.ToString(), NuevaVersion = r.NuevaVersion };

    private static ResultadoComandoConPlanResponse ConPlan(ResultadoComandoConPlan r) =>
        new() { ProyectoId = r.ProyectoId.ToString(), NuevaVersion = r.NuevaVersion, NuevaVersionPlan = r.NuevaVersionPlan };
}
