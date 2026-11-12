using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using static Reconstruccion.Dominio.Tests.Fabrica;

namespace Reconstruccion.Dominio.Tests.Planes;

public class PlanReconstruccionTests
{
    // --- Recepción de evaluaciones (RN-RE-02, 05, 18) -------------------------

    [Fact]
    public void Crea_una_necesidad_por_evaluacion_validada_con_intervencion()
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Guid.NewGuid());

        var resultados = plan.RecibirEvaluaciones(1, [Evaluacion(), Evaluacion()], Guid.NewGuid, Ahora);

        Assert.All(resultados, r => Assert.Equal(ResultadoRecepcion.Creada, r.Resultado));
        Assert.Equal(2, plan.Necesidades.Count);
        Assert.All(plan.Necesidades, n => Assert.Equal(EstadoNecesidad.Pendiente, n.Estado));
        Assert.Equal(1, plan.Version);
        Assert.IsType<NecesidadesRecibidas>(Assert.Single(plan.Eventos));
    }

    [Fact]
    public void Rechaza_evaluaciones_que_no_cumplen_el_contrato()
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Guid.NewGuid());

        var resultados = plan.RecibirEvaluaciones(1,
            [Evaluacion(validada: false), Evaluacion(requiereIntervencion: false)], Guid.NewGuid, Ahora);

        Assert.All(resultados, r => Assert.Equal(ResultadoRecepcion.ContratoIncumplido, r.Resultado));
        Assert.Empty(plan.Necesidades);
        Assert.Equal(0, plan.Version);
    }

    [Fact]
    public void Un_reenvio_no_duplica_y_una_version_menor_es_obsoleta()
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Guid.NewGuid());
        var evaluacionId = Guid.NewGuid();
        plan.RecibirEvaluaciones(1, [Evaluacion(evaluacionId, version: 2)], Guid.NewGuid, Ahora);

        var reenvio = plan.RecibirEvaluaciones(1, [Evaluacion(evaluacionId, version: 2)], Guid.NewGuid, Ahora);
        var anterior = plan.RecibirEvaluaciones(1, [Evaluacion(evaluacionId, version: 1)], Guid.NewGuid, Ahora);

        Assert.Equal(ResultadoRecepcion.Repetida, Assert.Single(reenvio).Resultado);
        Assert.Equal(ResultadoRecepcion.Obsoleta, Assert.Single(anterior).Resultado);
        Assert.Single(plan.Necesidades);
        Assert.Equal(1, plan.Version);
    }

    // --- Asignación (RN-RE-01, 03, 05, 20) ------------------------------------

    [Fact]
    public void Una_necesidad_pertenece_a_un_solo_proyecto_no_terminal()
    {
        var plan = PlanConNecesidades(1, out var ids);
        plan.AsignarNecesidades(ids, Guid.NewGuid());

        ViolaRegla("RN-RE-03", () => plan.AsignarNecesidades(ids, Guid.NewGuid()));
    }

    [Fact]
    public void Liberar_devuelve_las_necesidades_a_Pendiente()
    {
        var plan = PlanConNecesidades(2, out var ids);
        var proyectoId = Guid.NewGuid();
        plan.AsignarNecesidades(ids, proyectoId);

        plan.LiberarNecesidades(proyectoId);

        Assert.All(plan.Necesidades, n => Assert.Equal(EstadoNecesidad.Pendiente, n.Estado));
        Assert.All(plan.Necesidades, n => Assert.Null(n.ProyectoId));
    }

    [Fact]
    public void No_se_asigna_una_necesidad_con_alerta_de_origen_activa()
    {
        var plan = PlanConNecesidades(1, out var ids);
        var evaluacionId = plan.Necesidades[0].Origen.EvaluacionId;
        plan.AplicarCambioEvaluacion(evaluacionId, 2, TipoCambioOrigen.Revocacion, null, Ahora);

        ViolaRegla("RN-RE-20", () => plan.AsignarNecesidades(ids, Guid.NewGuid()));
    }

    // --- Cambios de evaluación (RN-RE-20) -------------------------------------

    [Fact]
    public void Una_correccion_sobre_necesidad_asignada_se_traslada_al_proyecto()
    {
        var plan = PlanConNecesidades(1, out var ids);
        var proyectoId = Guid.NewGuid();
        plan.AsignarNecesidades(ids, proyectoId);
        var origen = plan.Necesidades[0].Origen;

        var resultado = plan.AplicarCambioEvaluacion(origen.EvaluacionId, 2, TipoCambioOrigen.Correccion,
            origen with { NivelDano = "Destrucción total" }, Ahora);

        Assert.Equal(proyectoId, resultado.ProyectoAfectadoId);
        Assert.False(resultado.AlertaGeneradaEnPlan);
        Assert.Equal("Destrucción total", plan.Necesidades[0].Origen.NivelDano);
    }

    [Fact]
    public void Una_correccion_sobre_necesidad_atendida_es_rectificacion()
    {
        var plan = PlanConNecesidades(1, out var ids);
        var proyectoId = Guid.NewGuid();
        plan.AsignarNecesidades(ids, proyectoId);
        plan.AtenderNecesidades(proyectoId, Ahora);

        var resultado = plan.AplicarCambioEvaluacion(plan.Necesidades[0].Origen.EvaluacionId, 2,
            TipoCambioOrigen.Revocacion, null, Ahora);

        Assert.True(resultado.RegistradoComoRectificacion);
        Assert.Empty(plan.Alertas);
        Assert.Equal(EstadoNecesidad.Atendida, plan.Necesidades[0].Estado);
    }

    [Fact]
    public void Un_cambio_con_version_igual_o_menor_es_obsoleto()
    {
        var plan = PlanConNecesidades(1, out _);
        var version = plan.Version;

        var resultado = plan.AplicarCambioEvaluacion(plan.Necesidades[0].Origen.EvaluacionId, 1,
            TipoCambioOrigen.Revocacion, null, Ahora);

        Assert.True(resultado.Obsoleto);
        Assert.Equal(version, plan.Version);
    }

    // --- Señal de fin y completitud (RN-RE-15, RN-RE-22) ----------------------

    [Fact]
    public void La_senal_de_fin_responde_faltan_evaluaciones_si_no_llegaron_todas()
    {
        var plan = PlanConNecesidades(2, out _);
        var version = plan.Version;

        var respuesta = plan.RecibirSenalFin(1, cantidadEsperada: 3, Ahora);

        Assert.Equal(ResultadoSenalFin.FaltanEvaluaciones, respuesta.Resultado);
        Assert.Equal(2, respuesta.EvaluacionesRecibidas);
        Assert.Equal(version, plan.Version);
        Assert.False(plan.SenalFinVigente);
    }

    [Fact]
    public void Con_la_senal_aceptada_el_plan_no_se_completa_mientras_queden_necesidades()
    {
        var plan = PlanConNecesidades(1, out _);

        var respuesta = plan.RecibirSenalFin(1, 1, Ahora);

        Assert.Equal(ResultadoSenalFin.Aceptada, respuesta.Resultado);
        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
    }

    [Fact]
    public void El_plan_se_completa_al_atender_la_ultima_necesidad_con_la_senal_vigente()
    {
        var plan = PlanConNecesidades(1, out var ids);
        var proyectoId = Guid.NewGuid();
        plan.AsignarNecesidades(ids, proyectoId);
        plan.RecibirSenalFin(1, 1, Ahora);
        plan.LimpiarEventos();

        plan.AtenderNecesidades(proyectoId, Ahora);

        Assert.Equal(EstadoPlan.Completado, plan.Estado);
        var completado = Assert.IsType<PlanCompletado>(Assert.Single(plan.Eventos));
        Assert.Equal(plan.Version, completado.VersionAgregado);
    }

    [Fact]
    public void Una_senal_de_un_ciclo_ya_aceptado_es_obsoleta()
    {
        var plan = PlanConNecesidades(1, out _);
        plan.RecibirSenalFin(1, 1, Ahora);

        Assert.Equal(ResultadoSenalFin.Obsoleta, plan.RecibirSenalFin(1, 1, Ahora).Resultado);
    }

    [Fact]
    public void Una_necesidad_nueva_reabre_el_plan_e_invalida_la_senal_de_fin()
    {
        var plan = PlanConNecesidades(1, out var ids);
        plan.DescartarNecesidad(ids[0], "No requiere obra", Ahora);
        plan.RecibirSenalFin(1, 1, Ahora);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
        plan.LimpiarEventos();

        plan.RecibirEvaluaciones(2, [Evaluacion()], Guid.NewGuid, Ahora);

        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
        Assert.False(plan.SenalFinVigente);
        Assert.Contains(plan.Eventos, e => e is PlanReabierto);
    }

    [Fact]
    public void Una_reactivacion_reabre_el_plan_pero_conserva_la_senal()
    {
        var plan = PlanConNecesidades(1, out var ids);
        plan.DescartarNecesidad(ids[0], "No requiere obra", Ahora);
        plan.RecibirSenalFin(1, 1, Ahora);

        plan.ReactivarNecesidad(ids[0], "Se confirmó el daño", Ahora);

        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
        Assert.True(plan.SenalFinVigente);
    }

    [Fact]
    public void Cierre_solo_cierra_el_plan_si_acepta_la_version_vigente()
    {
        var plan = PlanConNecesidades(1, out var ids);
        plan.DescartarNecesidad(ids[0], "No requiere obra", Ahora);
        plan.RecibirSenalFin(1, 1, Ahora);
        var versionNotificada = plan.Version;
        plan.ReactivarNecesidad(ids[0], "Se confirmó el daño", Ahora);

        Assert.False(plan.RegistrarAceptacionCierre(versionNotificada, Ahora));
        Assert.Equal(EstadoPlan.Abierto, plan.Estado);
    }

    [Fact]
    public void Con_el_plan_Cerrado_las_evaluaciones_se_rechazan_y_el_plan_no_se_reabre()
    {
        var plan = PlanConNecesidades(1, out var ids);
        plan.DescartarNecesidad(ids[0], "No requiere obra", Ahora);
        plan.RecibirSenalFin(1, 1, Ahora);
        Assert.True(plan.RegistrarAceptacionCierre(plan.Version, Ahora));

        var resultado = plan.RecibirEvaluaciones(2, [Evaluacion()], Guid.NewGuid, Ahora);

        Assert.Equal(ResultadoRecepcion.PlanCerrado, Assert.Single(resultado).Resultado);
        Assert.Equal(EstadoPlan.Cerrado, plan.Estado);
    }

    [Fact]
    public void Una_alerta_de_origen_activa_impide_completar_el_plan()
    {
        var plan = PlanConNecesidades(1, out _);
        plan.AplicarCambioEvaluacion(plan.Necesidades[0].Origen.EvaluacionId, 2, TipoCambioOrigen.Revocacion, null, Ahora);
        var alerta = Assert.Single(plan.Alertas);

        plan.RecibirSenalFin(1, 1, Ahora);
        Assert.Equal(EstadoPlan.Abierto, plan.Estado);

        plan.DescartarNecesidad(plan.Necesidades[0].Id, "Evaluación revocada", Ahora);

        Assert.False(alerta.EstaActiva);
        Assert.Equal(EstadoPlan.Completado, plan.Estado);
    }
}
