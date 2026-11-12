using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Consultas;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Aplicacion.Planes;
using Reconstruccion.Aplicacion.Proyectos;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Aplicacion.Tests.Memoria;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Aplicacion.Tests.CasosDeUso;

/// <summary>
/// Casos de uso complementarios: transiciones, cambios presupuestales, necesidades,
/// alertas, vigilancia y consultas. Los agregados se preparan directamente con el dominio.
/// </summary>
public class EtapaTresTests
{
    private readonly Entorno _e = new();
    private readonly Guid _desastre = Guid.NewGuid();
    private readonly Guid _formulador = Guid.NewGuid();
    private readonly Guid _responsable = Guid.NewGuid();
    private readonly Guid _autoridad = Guid.NewGuid();

    // --- Transiciones -----------------------------------------------------------

    [Fact]
    public async Task Rechazar_libera_las_necesidades_en_la_misma_operacion()
    {
        var (plan, proyecto) = Preparar(EstadoProyecto.Propuesto);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        var r = await Ok(new RechazarProyectoManejador(_e.Ejecutor, _e, _e).ManejarAsync(
            new RechazarProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version, plan.Version, "No es prioritario")));

        Assert.Equal(EstadoProyecto.Rechazado, proyecto.Estado);
        Assert.All(plan.Necesidades, n => Assert.Equal(EstadoNecesidad.Pendiente, n.Estado));
        Assert.Equal(plan.Version, r.NuevaVersionPlan);
    }

    [Fact]
    public async Task Solo_la_Autoridad_rechaza_y_cancela()
    {
        var (plan, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        _e.ComoUsuario(_responsable, Rol.Responsable);

        var r = await new CancelarProyectoManejador(_e.Ejecutor, _e, _e).ManejarAsync(
            new CancelarProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version, plan.Version, "Sin recursos"));

        Assert.Equal(CodigoError.NoAutorizado, r.Error!.Codigo);
        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
    }

    [Fact]
    public async Task Cancelar_libera_necesidades_y_anula_el_cambio_Solicitado()
    {
        var (plan, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var cambio = proyecto.SolicitarCambio(2000m, [new Aporte("Nacion", 2000m)], "Ajuste", _responsable, Guid.NewGuid(), _e.Ahora);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        await Ok(new CancelarProyectoManejador(_e.Ejecutor, _e, _e).ManejarAsync(
            new CancelarProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version, plan.Version, "Sin recursos")));

        Assert.Equal(EstadoCambioPresupuestal.Anulado, cambio.Estado);
        Assert.All(plan.Necesidades, n => Assert.Equal(EstadoNecesidad.Pendiente, n.Estado));
    }

    [Fact]
    public async Task Suspende_el_Responsable_asignado_pero_no_otro_Responsable()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var manejador = new SuspenderProyectoManejador(_e.Ejecutor, _e);

        _e.ComoUsuario(Guid.NewGuid(), Rol.Responsable);
        var ajeno = await manejador.ManejarAsync(new SuspenderProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version, "Lluvias"));
        _e.ComoUsuario(_responsable, Rol.Responsable);
        await Ok(manejador.ManejarAsync(new SuspenderProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version, "Lluvias")));

        Assert.Equal(CodigoError.NoAutorizado, ajeno.Error!.Codigo);
        Assert.Equal(EstadoProyecto.Suspendido, proyecto.Estado);
    }

    [Fact]
    public async Task Al_reanudar_se_alertan_los_hitos_vencidos_durante_la_suspension()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        proyecto.Suspender("Orden público", _e.Ahora);
        _e.Ahora = _e.Ahora.AddDays(15);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        await Ok(new ReanudarProyectoManejador(_e.Ejecutor, _e).ManejarAsync(
            new ReanudarProyecto(Guid.NewGuid(), proyecto.Id, proyecto.Version)));

        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
        Assert.Contains(_e.Outbox, ev => ev is AlertaGenerada { Tipo: TipoAlerta.HitoVencido });
    }

    [Fact]
    public async Task Reprogramar_el_hito_resuelve_su_alerta_de_vencido()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        _e.Ahora = _e.Ahora.AddDays(15);
        proyecto.RevisarHitosVencidos(_e.Ahora);
        var hito = proyecto.Hitos[0];
        _e.ComoUsuario(_responsable, Rol.Responsable);

        await Ok(new ReprogramarHitoManejador(_e.Ejecutor, _e).ManejarAsync(new ReprogramarHito(
            Guid.NewGuid(), proyecto.Id, proyecto.Version, hito.Id, hito.FechaInicio, _e.Hoy.AddDays(5), "Lluvias")));

        Assert.False(proyecto.TieneAlertaActiva(TipoAlerta.HitoVencido));
    }

    // --- Cambios presupuestales -------------------------------------------------

    [Fact]
    public async Task El_Responsable_solicita_y_otra_Autoridad_aprueba_el_cambio()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        _e.ComoUsuario(_responsable, Rol.Responsable);
        await Ok(new SolicitarCambioPresupuestoManejador(_e.Ejecutor, _e).ManejarAsync(new SolicitarCambioPresupuesto(
            Guid.NewGuid(), proyecto.Id, proyecto.Version, 1500m, [new AporteDto("Nacion", 1500m)], "Mayores cantidades")));
        var cambio = proyecto.CambioSolicitado!;
        Assert.Equal(1000m, proyecto.Presupuesto);

        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        await Ok(new ResolverCambioPresupuestoManejador(_e.Ejecutor, _e).ManejarAsync(new ResolverCambioPresupuesto(
            Guid.NewGuid(), proyecto.Id, proyecto.Version, cambio.Id, Aprobar: true, null)));

        Assert.Equal(1500m, proyecto.Presupuesto);
        Assert.Contains(_e.Auditoria, a => a.Accion == nameof(ResolverCambioPresupuesto) && a.Detalle!.Contains("1000"));
    }

    [Fact]
    public async Task Quien_solicita_no_resuelve_su_propio_cambio()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        await Ok(new SolicitarCambioPresupuestoManejador(_e.Ejecutor, _e).ManejarAsync(new SolicitarCambioPresupuesto(
            Guid.NewGuid(), proyecto.Id, proyecto.Version, 1500m, [new AporteDto("Nacion", 1500m)], "Ajuste")));

        var r = await new ResolverCambioPresupuestoManejador(_e.Ejecutor, _e).ManejarAsync(new ResolverCambioPresupuesto(
            Guid.NewGuid(), proyecto.Id, proyecto.Version, proyecto.CambioSolicitado!.Id, true, null));

        Assert.Equal(CodigoError.ReglaIncumplida, r.Error!.Codigo);
        Assert.Equal("RN-RE-08", r.Error.Regla);
    }

    [Fact]
    public async Task Solo_el_solicitante_retira_el_cambio()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", _responsable, Guid.NewGuid(), _e.Ahora);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        var r = await new RetirarCambioPresupuestoManejador(_e.Ejecutor, _e).ManejarAsync(
            new RetirarCambioPresupuesto(Guid.NewGuid(), proyecto.Id, proyecto.Version, cambio.Id, "Ya no aplica"));

        Assert.Equal("RN-RE-09", r.Error!.Regla);
        Assert.Equal(EstadoCambioPresupuestal.Solicitado, cambio.Estado);
    }

    // --- Necesidades y alertas --------------------------------------------------

    [Fact]
    public async Task Descartar_la_ultima_necesidad_completa_el_plan_y_notifica()
    {
        var plan = Plan(1);
        plan.RecibirSenalFin(1, 1, _e.Ahora);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        var necesidadId = plan.Necesidades[0].Id;

        var r = await Ok(new DescartarNecesidadManejador(_e.Ejecutor, _e).ManejarAsync(
            new DescartarNecesidad(Guid.NewGuid(), _desastre, plan.Version, necesidadId, "No requiere obra")));

        Assert.Equal(necesidadId, r.Id);
        Assert.Equal(plan.Version, r.NuevaVersion);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
        Assert.Contains(_e.Outbox, ev => ev is PlanCompletado);
    }

    [Fact]
    public async Task Reactivar_reabre_el_plan_Completado()
    {
        var plan = Plan(1);
        plan.DescartarNecesidad(plan.Necesidades[0].Id, "No requiere obra", _e.Ahora);
        plan.RecibirSenalFin(1, 1, _e.Ahora);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        await Ok(new ReactivarNecesidadManejador(_e.Ejecutor, _e).ManejarAsync(new ReactivarNecesidad(
            Guid.NewGuid(), _desastre, plan.Version, plan.Necesidades[0].Id, "Se confirmó el daño")));

        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
        Assert.Contains(_e.Outbox, ev => ev is PlanReabierto);
    }

    [Fact]
    public async Task La_Autoridad_resuelve_una_alerta_de_origen_en_el_plan()
    {
        var plan = Plan(1);
        plan.AplicarCambioEvaluacion(plan.Necesidades[0].Origen.EvaluacionId, 2, TipoCambioOrigen.Revocacion, null, _e.Ahora);
        var alerta = Assert.Single(plan.Alertas);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        var r = await Ok(new ResolverAlertaManejador(_e.Ejecutor, _e, _e).ManejarAsync(new ResolverAlerta(
            Guid.NewGuid(), UbicacionAlerta.Plan, _desastre, plan.Version, alerta.Id, "Se mantiene la necesidad")));

        Assert.Equal(alerta.Id, r.Id);
        Assert.False(alerta.EstaActiva);
    }

    [Fact]
    public async Task La_Autoridad_resuelve_una_alerta_de_origen_en_el_proyecto()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        proyecto.RegistrarAlertaOrigen(proyecto.NecesidadIds[0], _e.Ahora);
        var alerta = Assert.Single(proyecto.Alertas);
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        await Ok(new ResolverAlertaManejador(_e.Ejecutor, _e, _e).ManejarAsync(new ResolverAlerta(
            Guid.NewGuid(), UbicacionAlerta.Proyecto, proyecto.Id, proyecto.Version, alerta.Id, "Se mantiene el alcance")));

        Assert.False(proyecto.TieneAlertaActiva(TipoAlerta.OrigenModificado));
    }

    // --- Vigilancia del cronograma ------------------------------------------------

    [Fact]
    public async Task La_vigilancia_genera_alertas_de_hitos_vencidos()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        _e.Ahora = _e.Ahora.AddDays(15);
        _e.ComoUsuario(Guid.NewGuid(), Rol.Sistema);

        var r = await Ok(new VigilanciaCronogramaManejador(_e.Ejecutor, _e).ManejarAsync(new EjecutarVigilanciaCronograma()));

        Assert.Equal(1, r.ProyectosRevisados);
        Assert.Equal(1, r.AlertasGeneradas);
        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
        Assert.Contains(_e.Outbox, ev => ev is AlertaGenerada);
    }

    [Fact]
    public async Task La_vigilancia_solo_la_ejecuta_el_Sistema()
    {
        _e.ComoUsuario(_autoridad, Rol.Autoridad);

        var r = await new VigilanciaCronogramaManejador(_e.Ejecutor, _e).ManejarAsync(new EjecutarVigilanciaCronograma());

        Assert.Equal(CodigoError.NoAutorizado, r.Error!.Codigo);
    }

    // --- Consultas ------------------------------------------------------------------

    [Fact]
    public async Task El_seguimiento_consolidado_es_de_la_Autoridad()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var lectura = new LecturaEnMemoria(_e);

        _e.ComoUsuario(_responsable, Rol.Responsable);
        var negado = await new ConsultarSeguimientoConsolidadoManejador(lectura, _e.Usuario).ManejarAsync(new ConsultarSeguimientoConsolidado(_desastre));
        _e.ComoUsuario(_autoridad, Rol.Autoridad);
        var permitido = await Ok(new ConsultarSeguimientoConsolidadoManejador(lectura, _e.Usuario).ManejarAsync(new ConsultarSeguimientoConsolidado(_desastre)));

        Assert.Equal(CodigoError.NoAutorizado, negado.Error!.Codigo);
        Assert.Equal(proyecto.Presupuesto, permitido.PresupuestoVigente);
        Assert.Equal(1, permitido.ProyectosPorEstado[EstadoProyecto.EnEjecucion]);
    }

    [Fact]
    public async Task El_Responsable_solo_ve_el_detalle_de_sus_proyectos()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var lectura = new LecturaEnMemoria(_e);

        _e.ComoUsuario(Guid.NewGuid(), Rol.Responsable);
        var ajeno = await new ConsultarDetalleProyectoManejador(lectura, _e.Usuario).ManejarAsync(new ConsultarDetalleProyecto(proyecto.Id));
        _e.ComoUsuario(_responsable, Rol.Responsable);
        var propio = await Ok(new ConsultarDetalleProyectoManejador(lectura, _e.Usuario).ManejarAsync(new ConsultarDetalleProyecto(proyecto.Id)));

        Assert.Equal(CodigoError.NoAutorizado, ajeno.Error!.Codigo);
        Assert.Equal(proyecto.Id, propio.ProyectoId);
    }

    [Fact]
    public async Task El_historial_valida_la_pagina_y_limita_al_Responsable()
    {
        var (_, proyecto) = Preparar(EstadoProyecto.EnEjecucion);
        var lectura = new LecturaEnMemoria(_e);
        _e.ComoUsuario(Guid.NewGuid(), Rol.Responsable);

        var invalida = await new ConsultarHistorialAuditoriaManejador(lectura, _e.Usuario).ManejarAsync(
            new ConsultarHistorialAuditoria(null, null, null, null, Pagina: 0));
        var ajeno = await new ConsultarHistorialAuditoriaManejador(lectura, _e.Usuario).ManejarAsync(
            new ConsultarHistorialAuditoria(null, proyecto.Id, null, null));

        Assert.Equal(CodigoError.DatosInvalidos, invalida.Error!.Codigo);
        Assert.Equal(CodigoError.NoAutorizado, ajeno.Error!.Codigo);
    }

    // --- Preparación ------------------------------------------------------------------

    private PlanReconstruccion Plan(int necesidades)
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), _desastre);
        plan.RecibirEvaluaciones(1, Enumerable.Range(0, necesidades).Select(_ => new EvaluacionEntrante(
            new EvaluacionOrigen(Guid.NewGuid(), 1, Guid.NewGuid(), "Puente", "Grave", _e.Ahora.AddDays(-3), new Ubicacion(5, -75, null)),
            true, true)).ToList(), Guid.NewGuid, _e.Ahora);
        plan.LimpiarEventos();
        _e.Planes.Add(plan);
        return plan;
    }

    /// <summary>Plan con una necesidad asignada a un proyecto en el estado pedido (Propuesto o EnEjecucion).</summary>
    private (PlanReconstruccion Plan, ProyectoReconstruccion Proyecto) Preparar(EstadoProyecto estado)
    {
        var plan = Plan(1);
        var proyectoId = Guid.NewGuid();
        plan.AsignarNecesidades([plan.Necesidades[0].Id], proyectoId);
        var proyecto = ProyectoReconstruccion.Proponer(proyectoId, _desastre, _formulador, "Puente La Esperanza", "",
            TipoProyecto.Reconstruccion, [plan.Necesidades[0].Id], _e.Ahora);

        if (estado != EstadoProyecto.Propuesto)
        {
            proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 1000m)]);
            proyecto.AsignarParticipantes(new Responsable(_responsable, Guid.NewGuid()), new Contratista("900", "Obras SAS"), _e.Ahora);
            proyecto.DefinirCronograma(_e.Hoy.AddDays(-10), _e.Hoy.AddDays(60),
                [("Cimentación", _e.Hoy.AddDays(-10), _e.Hoy.AddDays(10), 50m), ("Estructura", _e.Hoy.AddDays(11), _e.Hoy.AddDays(60), 50m)],
                Guid.NewGuid);
            proyecto.Aprobar(_autoridad, _e.Ahora);
            proyecto.Iniciar(_e.Ahora);
        }

        proyecto.LimpiarEventos();
        plan.LimpiarEventos();
        _e.Proyectos.Add(proyecto);
        return (plan, proyecto);
    }

    private static async Task<T> Ok<T>(Task<Resultado<T>> tarea)
    {
        var resultado = await tarea;
        Assert.True(resultado.EsExitoso, resultado.Error?.ToString());
        return resultado.Valor;
    }
}
