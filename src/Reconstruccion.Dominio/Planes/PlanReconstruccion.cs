using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Planes;

/// <summary>
/// Agregado PlanReconstruccion: uno por desastre, contiene sus necesidades (sección 3).
/// Al estar dentro del plan, la exclusividad de una necesidad y la completitud del plan
/// se verifican en un solo lugar.
/// </summary>
public sealed class PlanReconstruccion : AgregadoRaiz
{
    private readonly List<Necesidad> _necesidades = [];
    private readonly List<Alerta> _alertas = [];

    private PlanReconstruccion()
    {
    }

    private PlanReconstruccion(Guid id, Guid desastreId)
        : base(id)
    {
        DesastreId = desastreId;
        Estado = EstadoPlan.Abierto;
    }

    public Guid DesastreId { get; private set; }

    public EstadoPlan Estado { get; private set; }

    public IReadOnlyList<Necesidad> Necesidades => _necesidades;

    /// <summary>Alertas de origen modificado sobre necesidades no asignadas (RN-RE-20).</summary>
    public IReadOnlyList<Alerta> Alertas => _alertas;

    /// <summary>Versión del último ciclo cuya señal de fin se aceptó (RN-RE-22).</summary>
    public int? CicloAceptado { get; private set; }

    /// <summary>
    /// La señal de fin aceptada sigue representando el conjunto de evaluaciones.
    /// Una necesidad nueva la invalida (RN-RE-15).
    /// </summary>
    public bool SenalFinVigente { get; private set; }

    public static PlanReconstruccion Abrir(Guid id, Guid desastreId)
    {
        Validar.Id(desastreId, "El desastre del plan");
        return new PlanReconstruccion(id, desastreId);
    }

    public Necesidad ObtenerNecesidad(Guid necesidadId) =>
        _necesidades.FirstOrDefault(n => n.Id == necesidadId)
        ?? throw new ElementoNoEncontradoException($"La necesidad {necesidadId} no existe en el plan.");

    // ------------------------------------------------------------------------
    // Contrato con Caracterización
    // ------------------------------------------------------------------------

    /// <summary>
    /// Recibe evaluaciones de un ciclo de caracterización (RN-RE-02, 04, 05, 18 y 22).
    /// Un reenvío no duplica necesidades y una versión menor se registra como obsoleta.
    /// </summary>
    public IReadOnlyList<RecepcionEvaluacion> RecibirEvaluaciones(
        int versionCiclo,
        IReadOnlyList<EvaluacionEntrante> evaluaciones,
        Func<Guid> nuevoId,
        DateTimeOffset ahora)
    {
        if (versionCiclo <= 0)
        {
            throw new DatosInvalidosException("La versión del ciclo debe ser positiva.");
        }

        if (Estado == EstadoPlan.Cerrado)
        {
            return evaluaciones
                .Select(e => new RecepcionEvaluacion(e.Origen.EvaluacionId, ResultadoRecepcion.PlanCerrado, null))
                .ToList();
        }

        var resultados = new List<RecepcionEvaluacion>();
        var creadas = new List<Guid>();
        var huboCambios = false;
        var alertasNuevas = new List<Alerta>();

        foreach (var entrante in evaluaciones)
        {
            var origen = entrante.Origen;

            if (!entrante.Validada || !entrante.RequiereIntervencion || origen.EvaluacionId == Guid.Empty)
            {
                resultados.Add(new RecepcionEvaluacion(origen.EvaluacionId, ResultadoRecepcion.ContratoIncumplido, null));
                continue;
            }

            var existente = _necesidades.FirstOrDefault(n => n.Origen.EvaluacionId == origen.EvaluacionId);
            if (existente is null)
            {
                var necesidad = new Necesidad(nuevoId(), origen, versionCiclo);
                _necesidades.Add(necesidad);
                creadas.Add(necesidad.Id);
                resultados.Add(new RecepcionEvaluacion(origen.EvaluacionId, ResultadoRecepcion.Creada, necesidad.Id));
                continue;
            }

            if (origen.VersionEvaluacion < existente.Origen.VersionEvaluacion)
            {
                resultados.Add(new RecepcionEvaluacion(origen.EvaluacionId, ResultadoRecepcion.Obsoleta, existente.Id));
                continue;
            }

            if (existente.CicloRecepcion < versionCiclo)
            {
                existente.MarcarRecibidaEnCiclo(versionCiclo);
                huboCambios = true;
            }

            Guid? proyectoAfectado = null;
            if (origen.VersionEvaluacion > existente.Origen.VersionEvaluacion)
            {
                // Una versión mayor por este canal se trata como corrección (RN-RE-20).
                var alerta = AplicarCorreccion(existente, origen, ahora, out proyectoAfectado);
                if (alerta is not null)
                {
                    alertasNuevas.Add(alerta);
                }

                huboCambios = true;
            }

            resultados.Add(new RecepcionEvaluacion(origen.EvaluacionId, ResultadoRecepcion.Repetida, existente.Id, proyectoAfectado));
        }

        if (creadas.Count == 0 && !huboCambios)
        {
            return resultados;
        }

        IncrementarVersion();

        foreach (var alerta in alertasNuevas)
        {
            Registrar(new AlertaGenerada(DesastreId, Id, Version, ahora, alerta.Id, alerta.Tipo, alerta.ObjetoId, EnPlan: true));
        }

        if (creadas.Count > 0)
        {
            // Una necesidad nueva cambia el conjunto de evaluaciones: invalida la señal de fin (RN-RE-15).
            SenalFinVigente = false;
            Registrar(new NecesidadesRecibidas(DesastreId, Id, Version, ahora, creadas));
            if (Estado == EstadoPlan.Completado)
            {
                Reabrir(ahora);
            }
        }

        return resultados;
    }

    /// <summary>
    /// Aplica una corrección o revocación de Caracterización (RN-RE-20). La necesidad no se
    /// elimina: se actualiza su copia y se genera una alerta de origen modificado, salvo que
    /// la necesidad ya esté Atendida o Descartada, o el plan Cerrado, en cuyo caso se registra
    /// como rectificación (RN-RE-14).
    /// </summary>
    public ResultadoCambioOrigen AplicarCambioEvaluacion(
        Guid evaluacionId,
        long versionEvaluacion,
        TipoCambioOrigen tipo,
        EvaluacionOrigen? datosCorregidos,
        DateTimeOffset ahora)
    {
        var necesidad = _necesidades.FirstOrDefault(n => n.Origen.EvaluacionId == evaluacionId)
            ?? throw new ElementoNoEncontradoException($"No hay una necesidad para la evaluación {evaluacionId}.");

        if (versionEvaluacion <= necesidad.Origen.VersionEvaluacion)
        {
            return new ResultadoCambioOrigen(necesidad.Id, false, null, false, Obsoleto: true);
        }

        if (tipo == TipoCambioOrigen.Correccion && datosCorregidos is null)
        {
            throw new DatosInvalidosException("Una corrección debe traer los datos corregidos.");
        }

        IncrementarVersion();

        var esRectificacion = Estado == EstadoPlan.Cerrado
                              || necesidad.Estado is EstadoNecesidad.Atendida or EstadoNecesidad.Descartada;

        if (tipo == TipoCambioOrigen.Revocacion)
        {
            necesidad.MarcarRevocada(versionEvaluacion);
        }
        else
        {
            necesidad.ActualizarOrigen(datosCorregidos! with { EvaluacionId = evaluacionId, VersionEvaluacion = versionEvaluacion });
        }

        if (esRectificacion)
        {
            return new ResultadoCambioOrigen(necesidad.Id, false, null, RegistradoComoRectificacion: true, Obsoleto: false);
        }

        if (necesidad.Estado == EstadoNecesidad.Asignada)
        {
            // La alerta vive en el proyecto, que es quien no puede finalizarse (RN-RE-12, RN-RE-20).
            return new ResultadoCambioOrigen(necesidad.Id, false, necesidad.ProyectoId, false, false);
        }

        var alerta = GenerarAlertaOrigen(necesidad, ahora);
        if (alerta is not null)
        {
            Registrar(new AlertaGenerada(DesastreId, Id, Version, ahora, alerta.Id, alerta.Tipo, alerta.ObjetoId, EnPlan: true));
        }

        return new ResultadoCambioOrigen(necesidad.Id, AlertaGeneradaEnPlan: true, null, false, false);
    }

    /// <summary>
    /// Señal de fin de caracterización (RN-RE-22). Se acepta solo si ya llegaron las
    /// evaluaciones esperadas del ciclo, de modo que el resultado no depende del orden.
    /// </summary>
    public RespuestaSenalFin RecibirSenalFin(int versionCiclo, int cantidadEsperada, DateTimeOffset ahora)
    {
        if (versionCiclo <= 0 || cantidadEsperada < 0)
        {
            throw new DatosInvalidosException("La versión del ciclo debe ser positiva y la cantidad no negativa.");
        }

        var recibidas = _necesidades.Count(n => n.CicloRecepcion == versionCiclo);

        if (Estado == EstadoPlan.Cerrado || (CicloAceptado is { } aceptado && versionCiclo <= aceptado))
        {
            return new RespuestaSenalFin(ResultadoSenalFin.Obsoleta, recibidas);
        }

        if (recibidas < cantidadEsperada)
        {
            return new RespuestaSenalFin(ResultadoSenalFin.FaltanEvaluaciones, recibidas);
        }

        IncrementarVersion();
        CicloAceptado = versionCiclo;
        SenalFinVigente = true;
        EvaluarCompletitud(ahora);

        return new RespuestaSenalFin(ResultadoSenalFin.Aceptada, recibidas);
    }

    /// <summary>Cierre aceptó la notificación; solo cierra si la versión aceptada es la vigente (RN-RE-15).</summary>
    public bool RegistrarAceptacionCierre(long versionAceptada, DateTimeOffset ahora)
    {
        if (Estado != EstadoPlan.Completado || versionAceptada != Version)
        {
            return false;
        }

        IncrementarVersion();
        Estado = EstadoPlan.Cerrado;
        Registrar(new PlanCerrado(DesastreId, Id, Version, ahora));
        return true;
    }

    // ------------------------------------------------------------------------
    // Coordinación con proyectos (sección 7)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Asigna necesidades Pendientes a un proyecto nuevo (RN-RE-01, 03, 05 y 20).
    /// </summary>
    public void AsignarNecesidades(IReadOnlyCollection<Guid> necesidadIds, Guid proyectoId)
    {
        Validar.Regla(Estado == EstadoPlan.Abierto, "RN-RE-05", "Solo se crean proyectos con el plan Abierto.");
        Validar.Regla(necesidadIds.Count > 0, "RN-RE-01", "Un proyecto requiere al menos una necesidad.");
        Validar.Regla(necesidadIds.Distinct().Count() == necesidadIds.Count, "RN-RE-01", "Las necesidades no pueden repetirse.");

        var necesidades = necesidadIds.Select(ObtenerNecesidad).ToList();
        foreach (var necesidad in necesidades)
        {
            Validar.Regla(necesidad.Estado == EstadoNecesidad.Pendiente, "RN-RE-03",
                $"La necesidad {necesidad.Id} no está Pendiente.");
            Validar.Regla(!TieneAlertaActiva(necesidad.Id), "RN-RE-20",
                $"La necesidad {necesidad.Id} tiene una alerta de origen modificado activa.");
        }

        IncrementarVersion();
        foreach (var necesidad in necesidades)
        {
            necesidad.Asignar(proyectoId);
        }
    }

    /// <summary>El proyecto se rechazó o canceló: sus necesidades vuelven a Pendiente (RN-RE-03).</summary>
    public void LiberarNecesidades(Guid proyectoId)
    {
        var asignadas = NecesidadesDe(proyectoId);
        if (asignadas.Count == 0)
        {
            return;
        }

        IncrementarVersion();
        foreach (var necesidad in asignadas)
        {
            necesidad.Liberar();
        }
    }

    /// <summary>El proyecto se finalizó: sus necesidades pasan a Atendida y no vuelven a cambiar (RN-RE-03).</summary>
    public void AtenderNecesidades(Guid proyectoId, DateTimeOffset ahora)
    {
        var asignadas = NecesidadesDe(proyectoId);
        Validar.Regla(asignadas.Count > 0, "RN-RE-03", "El proyecto no tiene necesidades asignadas en el plan.");

        IncrementarVersion();
        foreach (var necesidad in asignadas)
        {
            necesidad.Atender();
        }

        EvaluarCompletitud(ahora);
    }

    // ------------------------------------------------------------------------
    // Decisiones de la Autoridad
    // ------------------------------------------------------------------------

    /// <summary>Descarta una necesidad Pendiente con justificación; resuelve su alerta de origen (RN-RE-20).</summary>
    public void DescartarNecesidad(Guid necesidadId, string justificacion, DateTimeOffset ahora)
    {
        justificacion = Validar.Texto(justificacion, "La justificación");
        var necesidad = ObtenerNecesidad(necesidadId);
        Validar.Regla(necesidad.Estado == EstadoNecesidad.Pendiente, "RN-RE-20", "Solo se descarta una necesidad Pendiente.");

        IncrementarVersion();
        necesidad.Descartar(justificacion);
        Registrar(new NecesidadDescartada(DesastreId, Id, Version, ahora, necesidadId));

        var alerta = AlertaActiva(necesidadId);
        if (alerta is not null)
        {
            alerta.Resolver(ahora, justificacion);
            Registrar(new AlertaResuelta(DesastreId, Id, Version, ahora, alerta.Id, alerta.Tipo, EnPlan: true));
        }

        EvaluarCompletitud(ahora);
    }

    /// <summary>Reactiva una necesidad Descartada; si el plan estaba Completado, se reabre (RN-RE-15).</summary>
    public void ReactivarNecesidad(Guid necesidadId, string justificacion, DateTimeOffset ahora)
    {
        justificacion = Validar.Texto(justificacion, "La justificación");
        Validar.Regla(Estado != EstadoPlan.Cerrado, "RN-RE-05", "El plan está Cerrado.");
        var necesidad = ObtenerNecesidad(necesidadId);
        Validar.Regla(necesidad.Estado == EstadoNecesidad.Descartada, "RN-RE-15", "Solo se reactiva una necesidad Descartada.");

        IncrementarVersion();
        necesidad.Reactivar(justificacion);
        Registrar(new NecesidadReactivada(DesastreId, Id, Version, ahora, necesidadId));

        if (Estado == EstadoPlan.Completado)
        {
            Reabrir(ahora);
        }
    }

    /// <summary>La Autoridad resuelve una alerta de origen modificado con justificación (RN-RE-21).</summary>
    public void ResolverAlerta(Guid alertaId, string justificacion, DateTimeOffset ahora)
    {
        justificacion = Validar.Texto(justificacion, "La justificación");
        var alerta = _alertas.FirstOrDefault(a => a.Id == alertaId)
            ?? throw new ElementoNoEncontradoException($"La alerta {alertaId} no existe en el plan.");
        Validar.Regla(alerta.EstaActiva, "RN-RE-21", "La alerta ya está resuelta.");

        IncrementarVersion();
        alerta.Resolver(ahora, justificacion);
        Registrar(new AlertaResuelta(DesastreId, Id, Version, ahora, alerta.Id, alerta.Tipo, EnPlan: true));
        EvaluarCompletitud(ahora);
    }

    // ------------------------------------------------------------------------
    // Reglas internas
    // ------------------------------------------------------------------------

    public bool TieneAlertaActiva(Guid necesidadId) => AlertaActiva(necesidadId) is not null;

    /// <summary>
    /// RN-RE-15: el plan pasa a Completado con la señal de fin vigente, sin necesidades
    /// Pendientes ni Asignadas y sin alertas de origen activas.
    /// </summary>
    private void EvaluarCompletitud(DateTimeOffset ahora)
    {
        if (Estado != EstadoPlan.Abierto || !SenalFinVigente)
        {
            return;
        }

        var quedanPorAtender = _necesidades.Any(n => n.Estado is EstadoNecesidad.Pendiente or EstadoNecesidad.Asignada);
        if (quedanPorAtender || _alertas.Any(a => a.EstaActiva))
        {
            return;
        }

        Estado = EstadoPlan.Completado;
        Registrar(new PlanCompletado(DesastreId, Id, Version, ahora));
    }

    private void Reabrir(DateTimeOffset ahora)
    {
        Estado = EstadoPlan.Abierto;
        Registrar(new PlanReabierto(DesastreId, Id, Version, ahora));
    }

    /// <summary>
    /// Corrección recibida por el canal de entrega. Devuelve la alerta generada en el plan,
    /// o en <paramref name="proyectoAfectado"/> el proyecto que debe registrarla.
    /// </summary>
    private Alerta? AplicarCorreccion(Necesidad necesidad, EvaluacionOrigen origen, DateTimeOffset ahora, out Guid? proyectoAfectado)
    {
        necesidad.ActualizarOrigen(origen);
        proyectoAfectado = null;

        if (necesidad.Estado is EstadoNecesidad.Atendida or EstadoNecesidad.Descartada)
        {
            return null; // Rectificación (RN-RE-14).
        }

        if (necesidad.Estado == EstadoNecesidad.Asignada)
        {
            proyectoAfectado = necesidad.ProyectoId;
            return null;
        }

        return GenerarAlertaOrigen(necesidad, ahora);
    }

    private Alerta? GenerarAlertaOrigen(Necesidad necesidad, DateTimeOffset ahora)
    {
        if (TieneAlertaActiva(necesidad.Id))
        {
            return null; // Una sola alerta activa por tipo y objeto (RN-RE-21).
        }

        var alerta = new Alerta(Guid.NewGuid(), TipoAlerta.OrigenModificado, necesidad.Id,
            $"La evaluación {necesidad.Origen.EvaluacionId} fue modificada en Caracterización.", ahora);
        _alertas.Add(alerta);
        return alerta;
    }

    private Alerta? AlertaActiva(Guid necesidadId) =>
        _alertas.FirstOrDefault(a => a.ObjetoId == necesidadId && a.EstaActiva);

    private List<Necesidad> NecesidadesDe(Guid proyectoId) =>
        _necesidades.Where(n => n.Estado == EstadoNecesidad.Asignada && n.ProyectoId == proyectoId).ToList();
}
