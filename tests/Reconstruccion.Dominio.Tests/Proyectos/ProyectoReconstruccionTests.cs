using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Proyectos;
using static Reconstruccion.Dominio.Tests.Fabrica;

namespace Reconstruccion.Dominio.Tests.Proyectos;

public class ProyectoReconstruccionTests
{
    // --- Aprobación (RN-RE-06) ------------------------------------------------

    [Fact]
    public void El_aprobador_debe_ser_distinto_del_formulador()
    {
        var proyecto = ProyectoPropuesto(out var formulador);
        proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 1000m)]);
        proyecto.AsignarParticipantes(new Responsable(Guid.NewGuid(), Guid.NewGuid()), null, Ahora);

        ViolaRegla("RN-RE-06", () => proyecto.Aprobar(formulador, Ahora));
    }

    [Fact]
    public void Los_aportes_deben_sumar_exactamente_el_presupuesto()
    {
        var proyecto = ProyectoPropuesto(out _);
        proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 999.99m)]);
        proyecto.AsignarParticipantes(new Responsable(Guid.NewGuid(), Guid.NewGuid()), null, Ahora);

        ViolaRegla("RN-RE-06", () => proyecto.Aprobar(Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Aprobar_requiere_responsable_asignado()
    {
        var proyecto = ProyectoPropuesto(out _);
        proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 1000m)]);

        ViolaRegla("RN-RE-06", () => proyecto.Aprobar(Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void Cada_aporte_identifica_una_fuente_distinta()
    {
        var proyecto = ProyectoPropuesto(out _);

        ViolaRegla("RN-RE-06", () => proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 500m), new Aporte("nacion", 500m)]));
    }

    [Fact]
    public void Los_montos_admiten_maximo_dos_decimales()
    {
        var proyecto = ProyectoPropuesto(out _);

        Assert.Throws<DatosInvalidosException>(() => proyecto.EditarPresupuesto(1000.005m, [new Aporte("Nacion", 1000.005m)]));
    }

    [Fact]
    public void Desde_Aprobado_el_presupuesto_no_se_edita_directamente()
    {
        var proyecto = ProyectoAprobado(out _);

        ViolaRegla("RN-RE-08", () => proyecto.EditarPresupuesto(2000m, [new Aporte("Nacion", 2000m)]));
    }

    // --- Cronograma e inicio (RN-RE-07) ---------------------------------------

    [Fact]
    public void Los_pesos_de_los_hitos_deben_sumar_100()
    {
        var proyecto = ProyectoPropuesto(out _);

        ViolaRegla("RN-RE-07", () => proyecto.DefinirCronograma(Hoy, Hoy.AddDays(30),
            [("Única etapa", Hoy, Hoy.AddDays(30), 90m)], Guid.NewGuid));
    }

    [Fact]
    public void Un_hito_no_puede_quedar_fuera_del_rango_del_cronograma()
    {
        var proyecto = ProyectoPropuesto(out _);

        ViolaRegla("RN-RE-07", () => proyecto.DefinirCronograma(Hoy, Hoy.AddDays(30),
            [("Única etapa", Hoy, Hoy.AddDays(31), 100m)], Guid.NewGuid));
    }

    [Fact]
    public void Iniciar_requiere_contratista()
    {
        var proyecto = ProyectoPropuesto(out _);
        proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 1000m)]);
        proyecto.AsignarParticipantes(new Responsable(Guid.NewGuid(), Guid.NewGuid()), null, Ahora);
        proyecto.DefinirCronograma(Hoy, Hoy.AddDays(30), [("Única etapa", Hoy, Hoy.AddDays(30), 100m)], Guid.NewGuid);
        proyecto.Aprobar(Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-07", () => proyecto.Iniciar(Ahora));
    }

    [Fact]
    public void Iniciar_cambia_el_estado_y_emite_el_evento()
    {
        var proyecto = ProyectoAprobado(out _);

        proyecto.Iniciar(Ahora);

        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
        var evento = Assert.IsType<ProyectoEstadoCambiado>(Assert.Single(proyecto.Eventos));
        Assert.Equal(EstadoProyecto.Aprobado, evento.EstadoAnterior);
        Assert.Equal(proyecto.Version, evento.VersionAgregado);
    }

    // --- Avance (RN-RE-10) ----------------------------------------------------

    [Fact]
    public void El_avance_del_proyecto_es_la_suma_ponderada_de_los_hitos()
    {
        var proyecto = ProyectoEnEjecucion(out _);

        proyecto.RegistrarAvance(proyecto.Hitos[0].Id, 50m, Hoy, [], null, Guid.NewGuid(), Ahora);
        proyecto.RegistrarAvance(proyecto.Hitos[1].Id, 25m, Hoy, [], null, Guid.NewGuid(), Ahora);

        // 60 × 50 / 100 + 40 × 25 / 100 = 30 + 10
        Assert.Equal(40m, proyecto.AvanceTotal);
    }

    [Fact]
    public void El_avance_no_disminuye_sin_motivo_de_correccion()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        var hito = proyecto.Hitos[0].Id;
        proyecto.RegistrarAvance(hito, 50m, Hoy, [], null, Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-10", () => proyecto.RegistrarAvance(hito, 40m, Hoy, [], null, Guid.NewGuid(), Ahora));

        var correccion = proyecto.RegistrarAvance(hito, 40m, Hoy, [], "Error de medición", Guid.NewGuid(), Ahora);
        Assert.Equal(50m, correccion.PorcentajeAnterior);
    }

    [Fact]
    public void El_avance_no_admite_fecha_efectiva_futura()
    {
        var proyecto = ProyectoEnEjecucion(out _);

        ViolaRegla("RN-RE-10", () => proyecto.RegistrarAvance(proyecto.Hitos[0].Id, 10m, Hoy.AddDays(1), [], null, Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void El_avance_conserva_sus_referencias_a_evidencias()
    {
        var proyecto = ProyectoEnEjecucion(out _);

        var avance = proyecto.RegistrarAvance(proyecto.Hitos[0].Id, 30m, Hoy,
            [new ReferenciaEvidencia("ev-1", "Fotografía", "evidencias/ev-1.jpg")], null, Guid.NewGuid(), Ahora);

        Assert.Single(avance.Evidencias);
    }

    [Fact]
    public void Un_proyecto_Suspendido_no_admite_avances_pero_si_costos()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.Suspender("Lluvias", Ahora);

        ViolaRegla("RN-RE-10", () => proyecto.RegistrarAvance(proyecto.Hitos[0].Id, 10m, Hoy, [], null, Guid.NewGuid(), Ahora));
        proyecto.RegistrarCosto(100m, "Vigilancia", Hoy, null, null, Guid.NewGuid(), Ahora);
        Assert.Equal(100m, proyecto.CostoEjecutado);
    }

    // --- Costos y sobrecosto (RN-RE-11, RN-RE-21) -----------------------------

    [Fact]
    public void Un_costo_que_supera_el_presupuesto_se_registra_y_genera_alerta()
    {
        var proyecto = ProyectoEnEjecucion(out _);

        proyecto.RegistrarCosto(1200m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);

        Assert.Equal(1200m, proyecto.CostoEjecutado);
        Assert.True(proyecto.TieneAlertaActiva(TipoAlerta.Sobrecosto));
    }

    [Fact]
    public void Un_reverso_que_devuelve_el_costo_al_presupuesto_resuelve_el_sobrecosto()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.RegistrarCosto(800m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);
        var errado = proyecto.RegistrarCosto(400m, "Factura duplicada", Hoy, null, null, Guid.NewGuid(), Ahora);
        Assert.True(proyecto.TieneAlertaActiva(TipoAlerta.Sobrecosto));

        proyecto.RegistrarCosto(0m, "Reverso", Hoy, errado.Id, "Factura duplicada", Guid.NewGuid(), Ahora);

        Assert.Equal(800m, proyecto.CostoEjecutado);
        Assert.False(proyecto.TieneAlertaActiva(TipoAlerta.Sobrecosto));
        Assert.Equal(3, proyecto.Costos.Count);
    }

    [Fact]
    public void Un_cambio_aprobado_que_cubre_el_costo_resuelve_el_sobrecosto()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.RegistrarCosto(1200m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);
        var solicitante = Guid.NewGuid();
        var cambio = proyecto.SolicitarCambio(1300m, [new Aporte("Nacion", 1000m), new Aporte("Departamento", 300m)],
            "Mayores cantidades", solicitante, Guid.NewGuid(), Ahora);

        proyecto.ResolverCambio(cambio.Id, aprobar: true, null, Guid.NewGuid(), Ahora);

        Assert.Equal(1300m, proyecto.Presupuesto);
        Assert.False(proyecto.TieneAlertaActiva(TipoAlerta.Sobrecosto));
    }

    [Fact]
    public void Un_costo_no_se_revierte_dos_veces()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        var costo = proyecto.RegistrarCosto(100m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);
        proyecto.RegistrarCosto(0m, "Reverso", Hoy, costo.Id, "Error", Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-11", () => proyecto.RegistrarCosto(0m, "Reverso", Hoy, costo.Id, "Error", Guid.NewGuid(), Ahora));
    }

    // --- Cambios presupuestales (RN-RE-08, RN-RE-09) --------------------------

    [Fact]
    public void Un_cambio_Solicitado_no_altera_el_presupuesto_vigente()
    {
        var proyecto = ProyectoAprobado(out _);

        proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        Assert.Equal(1000m, proyecto.Presupuesto);
        Assert.NotNull(proyecto.CambioSolicitado);
    }

    [Fact]
    public void Solo_hay_un_cambio_Solicitado_a_la_vez()
    {
        var proyecto = ProyectoAprobado(out _);
        proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-09", () => proyecto.SolicitarCambio(1600m, [new Aporte("Nacion", 1600m)], "Otro", Guid.NewGuid(), Guid.NewGuid(), Ahora));
    }

    [Fact]
    public void El_cambio_lo_resuelve_alguien_distinto_del_solicitante()
    {
        var proyecto = ProyectoAprobado(out _);
        var solicitante = Guid.NewGuid();
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", solicitante, Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-08", () => proyecto.ResolverCambio(cambio.Id, true, null, solicitante, Ahora));
    }

    [Fact]
    public void Aprobar_un_cambio_conserva_el_historial_y_avanza_la_version_del_presupuesto()
    {
        var proyecto = ProyectoAprobado(out _);
        var resolutor = Guid.NewGuid();
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        proyecto.ResolverCambio(cambio.Id, true, "De acuerdo", resolutor, Ahora);

        Assert.Equal(EstadoCambioPresupuestal.Aprobado, cambio.Estado);
        Assert.Equal(1000m, cambio.PresupuestoAnterior);
        Assert.Equal(resolutor, cambio.ResolutorId);
        Assert.Equal(1, proyecto.VersionPresupuesto);
        Assert.Equal(1500m, Assert.Single(proyecto.Aportes).Monto);
    }

    [Fact]
    public void Los_aportes_vigentes_son_copias_de_los_del_cambio_aprobado()
    {
        var proyecto = ProyectoAprobado(out _);
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        proyecto.ResolverCambio(cambio.Id, true, null, Guid.NewGuid(), Ahora);

        // Iguales en valor, pero no la misma instancia: cada owned tiene un solo dueño al persistir.
        Assert.Equal(cambio.NuevosAportes[0], proyecto.Aportes[0]);
        Assert.False(ReferenceEquals(cambio.NuevosAportes[0], proyecto.Aportes[0]));
    }

    [Fact]
    public void Solo_el_solicitante_retira_su_cambio()
    {
        var proyecto = ProyectoAprobado(out _);
        var solicitante = Guid.NewGuid();
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", solicitante, Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-09", () => proyecto.RetirarCambio(cambio.Id, "Ya no aplica", Guid.NewGuid(), Ahora));
        proyecto.RetirarCambio(cambio.Id, "Ya no aplica", solicitante, Ahora);

        Assert.Equal(EstadoCambioPresupuestal.Anulado, cambio.Estado);
    }

    [Fact]
    public void Cancelar_anula_el_cambio_Solicitado()
    {
        var proyecto = ProyectoAprobado(out _);
        var cambio = proyecto.SolicitarCambio(1500m, [new Aporte("Nacion", 1500m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        proyecto.Cancelar("Sin recursos", Ahora);

        Assert.Equal(EstadoProyecto.Cancelado, proyecto.Estado);
        Assert.Equal(EstadoCambioPresupuestal.Anulado, cambio.Estado);
    }

    // --- Finalizar (RN-RE-12) -------------------------------------------------

    [Fact]
    public void Finaliza_cuando_cumple_todas_las_condiciones()
    {
        var proyecto = ProyectoListoParaFinalizar();

        proyecto.Finalizar(Ahora);

        Assert.Equal(EstadoProyecto.Finalizado, proyecto.Estado);
        Assert.Equal(100m, proyecto.AvanceTotal);
    }

    [Fact]
    public void No_finaliza_con_hitos_incompletos()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.AdjuntarDocumento(TipoDocumento.ActaEntrega, "Acta", "docs/acta.pdf", Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-12", () => proyecto.Finalizar(Ahora));
    }

    [Fact]
    public void No_finaliza_sin_acta_de_entrega()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        foreach (var hito in proyecto.Hitos)
        {
            proyecto.RegistrarAvance(hito.Id, 100m, Hoy, [], null, Guid.NewGuid(), Ahora);
        }

        ViolaRegla("RN-RE-12", () => proyecto.Finalizar(Ahora));
    }

    [Fact]
    public void No_finaliza_con_sobrecosto()
    {
        var proyecto = ProyectoListoParaFinalizar();
        proyecto.RegistrarCosto(500m, "Adicional", Hoy, null, null, Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-12", () => proyecto.Finalizar(Ahora));
    }

    [Fact]
    public void No_finaliza_con_un_cambio_Solicitado()
    {
        var proyecto = ProyectoListoParaFinalizar();
        proyecto.SolicitarCambio(1100m, [new Aporte("Nacion", 1100m)], "Ajuste", Guid.NewGuid(), Guid.NewGuid(), Ahora);

        ViolaRegla("RN-RE-12", () => proyecto.Finalizar(Ahora));
    }

    [Fact]
    public void No_finaliza_con_alerta_de_origen_activa()
    {
        var necesidad = Guid.NewGuid();
        var proyecto = ProyectoListoParaFinalizar(necesidad);
        proyecto.RegistrarAlertaOrigen(necesidad, Ahora);

        ViolaRegla("RN-RE-12", () => proyecto.Finalizar(Ahora));
    }

    // --- Estados terminales y suspensión (RN-RE-13, 14, 20) -------------------

    [Fact]
    public void En_estado_terminal_solo_se_adjuntan_documentos_administrativos()
    {
        var proyecto = ProyectoAprobado(out _);
        proyecto.Cancelar("Sin recursos", Ahora);

        ViolaRegla("RN-RE-14", () => proyecto.AdjuntarDocumento(TipoDocumento.Contrato, "Contrato", "c.pdf", Guid.NewGuid(), Ahora));
        proyecto.AdjuntarDocumento(TipoDocumento.Administrativo, "Oficio", "o.pdf", Guid.NewGuid(), Ahora);
    }

    [Fact]
    public void Suspender_y_cancelar_exigen_motivo()
    {
        var proyecto = ProyectoEnEjecucion(out _);

        Assert.Throws<DatosInvalidosException>(() => proyecto.Suspender(" ", Ahora));
        Assert.Throws<DatosInvalidosException>(() => proyecto.Cancelar("", Ahora));
    }

    [Fact]
    public void No_se_reanuda_con_alerta_de_origen_activa()
    {
        var necesidad = Guid.NewGuid();
        var proyecto = ProyectoEnEjecucion(out _, necesidad);
        proyecto.Suspender("Revisión", Ahora);
        proyecto.RegistrarAlertaOrigen(necesidad, Ahora);

        ViolaRegla("RN-RE-20", () => proyecto.Reanudar(Ahora));

        var alerta = Assert.Single(proyecto.Alertas);
        proyecto.ResolverAlerta(alerta.Id, "Se mantiene el alcance", Ahora);
        proyecto.Reanudar(Ahora);
        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
    }

    // --- Hitos vencidos (RN-RE-21) --------------------------------------------

    [Fact]
    public void La_vigilancia_genera_una_sola_alerta_por_hito_vencido()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        var despuesDelPrimerHito = Ahora.AddDays(11);

        Assert.Equal(1, proyecto.RevisarHitosVencidos(despuesDelPrimerHito));
        Assert.Equal(0, proyecto.RevisarHitosVencidos(despuesDelPrimerHito));
        Assert.Equal(EstadoProyecto.EnEjecucion, proyecto.Estado);
    }

    [Fact]
    public void Completar_el_hito_resuelve_su_alerta_de_vencido()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.RevisarHitosVencidos(Ahora.AddDays(11));

        proyecto.RegistrarAvance(proyecto.Hitos[0].Id, 100m, Hoy, [], null, Guid.NewGuid(), Ahora.AddDays(11));

        Assert.False(proyecto.TieneAlertaActiva(TipoAlerta.HitoVencido));
    }

    [Fact]
    public void Al_reanudar_se_alertan_los_hitos_que_vencieron_durante_la_suspension()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.Suspender("Orden público", Ahora);
        Assert.Equal(0, proyecto.RevisarHitosVencidos(Ahora.AddDays(11)));

        proyecto.Reanudar(Ahora.AddDays(11));

        Assert.True(proyecto.TieneAlertaActiva(TipoAlerta.HitoVencido));
    }

    [Fact]
    public void Las_alertas_automaticas_no_se_resuelven_a_mano()
    {
        var proyecto = ProyectoEnEjecucion(out _);
        proyecto.RegistrarCosto(1200m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);
        var alerta = Assert.Single(proyecto.Alertas);

        ViolaRegla("RN-RE-21", () => proyecto.ResolverAlerta(alerta.Id, "Revisado", Ahora));
    }
}
