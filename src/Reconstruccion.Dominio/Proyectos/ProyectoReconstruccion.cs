using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Dominio.Proyectos;

/// <summary>
/// Agregado ProyectoReconstruccion (sección 3). Referencia sus necesidades por
/// identificador, sin asociación directa con entidades del plan.
/// </summary>
public sealed class ProyectoReconstruccion : AgregadoRaiz
{
    private static readonly EstadoProyecto[] EstadosTerminales =
        [EstadoProyecto.Rechazado, EstadoProyecto.Cancelado, EstadoProyecto.Finalizado];

    private readonly List<Guid> _necesidadIds = [];
    private readonly List<Aporte> _aportes = [];
    private readonly List<Hito> _hitos = [];
    private readonly List<Avance> _avances = [];
    private readonly List<Costo> _costos = [];
    private readonly List<Documento> _documentos = [];
    private readonly List<CambioPresupuestal> _cambios = [];
    private readonly List<Alerta> _alertas = [];

    private ProyectoReconstruccion()
    {
        Nombre = string.Empty;
        Descripcion = string.Empty;
    }

    private ProyectoReconstruccion(Guid id, Guid desastreId, Guid formuladorId, string nombre, string descripcion,
        TipoProyecto tipo, IEnumerable<Guid> necesidadIds)
        : base(id)
    {
        DesastreId = desastreId;
        FormuladorId = formuladorId;
        Nombre = nombre;
        Descripcion = descripcion;
        Tipo = tipo;
        Estado = EstadoProyecto.Propuesto;
        _necesidadIds.AddRange(necesidadIds);
    }

    public Guid DesastreId { get; private set; }

    public string Nombre { get; private set; }

    public string Descripcion { get; private set; }

    public TipoProyecto Tipo { get; private set; }

    public EstadoProyecto Estado { get; private set; }

    /// <summary>Quien creó el proyecto; lo opera mientras está en Propuesto (RN-RE-19).</summary>
    public Guid FormuladorId { get; private set; }

    /// <summary>Responsable asignado; lo opera desde Aprobado (RN-RE-19).</summary>
    public Responsable? Responsable { get; private set; }

    public Guid? ResponsableId => Responsable?.UsuarioId;

    public Contratista? Contratista { get; private set; }

    public decimal Presupuesto { get; private set; }

    /// <summary>Se incrementa con cada cambio de presupuesto vigente (RN-RE-08).</summary>
    public int VersionPresupuesto { get; private set; }

    public DateOnly? FechaInicio { get; private set; }

    public DateOnly? FechaFin { get; private set; }

    /// <summary>Último motivo de rechazo, suspensión o cancelación (RN-RE-13).</summary>
    public string? UltimoMotivo { get; private set; }

    public IReadOnlyList<Guid> NecesidadIds => _necesidadIds;

    public IReadOnlyList<Aporte> Aportes => _aportes;

    public IReadOnlyList<Hito> Hitos => _hitos;

    public IReadOnlyList<Avance> Avances => _avances;

    public IReadOnlyList<Costo> Costos => _costos;

    public IReadOnlyList<Documento> Documentos => _documentos;

    public IReadOnlyList<CambioPresupuestal> CambiosPresupuestales => _cambios;

    public IReadOnlyList<Alerta> Alertas => _alertas;

    public bool EsTerminal => EstadosTerminales.Contains(Estado);

    /// <summary>Costo ejecutado: suma de costos menos reversos.</summary>
    public decimal CostoEjecutado => _costos.Sum(c => c.MontoConSigno);

    /// <summary>Avance del proyecto: Σ (peso del hito × avance del hito) / 100 (RN-RE-10).</summary>
    public decimal AvanceTotal => _hitos.Sum(h => h.Peso * h.Avance) / 100m;

    public CambioPresupuestal? CambioSolicitado => _cambios.FirstOrDefault(c => c.Estado == EstadoCambioPresupuestal.Solicitado);

    // ------------------------------------------------------------------------
    // Creación y propuesta
    // ------------------------------------------------------------------------

    /// <summary>
    /// Crea el proyecto en Propuesto a partir de una o más necesidades del mismo desastre
    /// (RN-RE-01). Quien lo crea queda como formulador (RN-RE-19).
    /// </summary>
    public static ProyectoReconstruccion Proponer(Guid id, Guid desastreId, Guid formuladorId, string nombre,
        string descripcion, TipoProyecto tipo, IReadOnlyCollection<Guid> necesidadIds, DateTimeOffset ahora)
    {
        Validar.Id(desastreId, "El desastre del proyecto");
        Validar.Id(formuladorId, "El formulador");
        nombre = Validar.Texto(nombre, "El nombre del proyecto");
        Validar.Regla(necesidadIds.Count > 0, "RN-RE-01", "Un proyecto se crea a partir de una o más necesidades.");
        Validar.Regla(necesidadIds.Distinct().Count() == necesidadIds.Count, "RN-RE-01", "Las necesidades no pueden repetirse.");

        var proyecto = new ProyectoReconstruccion(id, desastreId, formuladorId, nombre, descripcion?.Trim() ?? string.Empty,
            tipo, necesidadIds);
        proyecto.Registrar(new ProyectoCreado(desastreId, id, proyecto.Version, ahora, necesidadIds.ToList()));
        return proyecto;
    }

    /// <summary>Edición directa de presupuesto y aportes, solo en Propuesto (RN-RE-08, sección 5.1).</summary>
    public void EditarPresupuesto(decimal presupuesto, IReadOnlyList<Aporte> aportes)
    {
        ExigirEstado("RN-RE-08", "En Propuesto se edita el presupuesto directamente; desde Aprobado se solicita un cambio.",
            EstadoProyecto.Propuesto);
        Validar.Monto(presupuesto, "El presupuesto", permitirCero: true);
        var aportesValidados = ValidarAportes(aportes);

        IncrementarVersion();
        Presupuesto = presupuesto;
        _aportes.Clear();
        _aportes.AddRange(aportesValidados);
    }

    /// <summary>Asigna responsable y/o contratista; no aplica en estados terminales (sección 5.1).</summary>
    public void AsignarParticipantes(Responsable? responsable, Contratista? contratista, DateTimeOffset ahora)
    {
        ExigirNoTerminal("RN-RE-14");
        if (responsable is null && contratista is null)
        {
            throw new DatosInvalidosException("Debe indicarse el responsable, el contratista o ambos.");
        }

        if (responsable is not null)
        {
            Validar.Id(responsable.UsuarioId, "El responsable");
            Validar.Id(responsable.OrganismoId, "El organismo del responsable");
        }

        if (contratista is not null)
        {
            contratista = new Contratista(Validar.Texto(contratista.Identificacion, "La identificación del contratista"),
                Validar.Texto(contratista.Nombre, "El nombre del contratista"));
        }

        IncrementarVersion();
        Responsable = responsable ?? Responsable;
        Contratista = contratista ?? Contratista;
        Registrar(new ParticipantesAsignados(DesastreId, Id, Version, ahora));
    }

    /// <summary>Define el cronograma en Propuesto o Aprobado (RN-RE-07, sección 5.1).</summary>
    public void DefinirCronograma(DateOnly inicio, DateOnly fin,
        IReadOnlyList<(string Nombre, DateOnly Inicio, DateOnly Fin, decimal Peso)> hitos, Func<Guid> nuevoId)
    {
        ExigirEstado("RN-RE-07", "El cronograma se define en Propuesto o Aprobado; después solo se reprograma.",
            EstadoProyecto.Propuesto, EstadoProyecto.Aprobado);

        Validar.Regla(inicio <= fin, "RN-RE-07", "La fecha de inicio no puede ser posterior a la de fin.");
        Validar.Regla(hitos.Count > 0, "RN-RE-07", "El cronograma requiere al menos un hito.");
        foreach (var hito in hitos)
        {
            Validar.Texto(hito.Nombre, "El nombre del hito");
            Validar.Regla(hito.Inicio <= hito.Fin, "RN-RE-07", $"El hito {hito.Nombre} tiene fechas invertidas.");
            Validar.Regla(hito.Inicio >= inicio && hito.Fin <= fin, "RN-RE-07",
                $"El hito {hito.Nombre} está fuera del rango del cronograma.");
            Validar.Regla(hito.Peso > 0, "RN-RE-07", $"El peso del hito {hito.Nombre} debe ser positivo.");
        }

        Validar.Regla(hitos.Sum(h => h.Peso) == 100, "RN-RE-07", "Los pesos de los hitos deben sumar 100.");

        IncrementarVersion();
        FechaInicio = inicio;
        FechaFin = fin;
        _hitos.Clear();
        _hitos.AddRange(hitos.Select(h => new Hito(nuevoId(), h.Nombre.Trim(), h.Inicio, h.Fin, h.Peso)));
    }

    // ------------------------------------------------------------------------
    // Transiciones de estado (sección 5.1)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Propuesto a Aprobado (RN-RE-06): presupuesto mayor que cero, aportes que suman
    /// exactamente el presupuesto, responsable asignado y aprobador distinto del formulador.
    /// </summary>
    public void Aprobar(Guid aprobadorId, DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-06", "Solo se aprueba un proyecto Propuesto.", EstadoProyecto.Propuesto);
        Validar.Regla(aprobadorId != FormuladorId, "RN-RE-06", "El aprobador debe ser distinto de quien formuló el proyecto.");
        Validar.Regla(Presupuesto > 0, "RN-RE-06", "El presupuesto debe ser mayor que cero.");
        Validar.Regla(_aportes.Count > 0 && _aportes.Sum(a => a.Monto) == Presupuesto, "RN-RE-06",
            "Los aportes de financiación deben sumar exactamente el presupuesto.");
        Validar.Regla(Responsable is not null, "RN-RE-06", "El proyecto requiere un responsable asignado.");

        CambiarEstado(EstadoProyecto.Aprobado, null, ahora);
    }

    /// <summary>Propuesto a Rechazado, con motivo; el caso de uso libera las necesidades (RN-RE-03, RN-RE-14).</summary>
    public void Rechazar(string motivo, DateTimeOffset ahora)
    {
        motivo = Validar.Texto(motivo, "El motivo del rechazo");
        ExigirEstado("RN-RE-14", "Solo se rechaza un proyecto Propuesto.", EstadoProyecto.Propuesto);
        CambiarEstado(EstadoProyecto.Rechazado, motivo, ahora);
    }

    /// <summary>Aprobado a EnEjecucion: contratista y cronograma válidos (RN-RE-07).</summary>
    public void Iniciar(DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-07", "Solo se inicia un proyecto Aprobado.", EstadoProyecto.Aprobado);
        Validar.Regla(Contratista is not null, "RN-RE-07", "Iniciar requiere un contratista asignado.");
        Validar.Regla(_hitos.Count > 0, "RN-RE-07", "Iniciar requiere un cronograma válido.");
        CambiarEstado(EstadoProyecto.EnEjecucion, null, ahora);
    }

    /// <summary>EnEjecucion a Suspendido, con motivo (RN-RE-13).</summary>
    public void Suspender(string motivo, DateTimeOffset ahora)
    {
        motivo = Validar.Texto(motivo, "El motivo de la suspensión");
        ExigirEstado("RN-RE-13", "Solo se suspende un proyecto EnEjecucion.", EstadoProyecto.EnEjecucion);
        CambiarEstado(EstadoProyecto.Suspendido, motivo, ahora);
    }

    /// <summary>
    /// Suspendido a EnEjecucion, sin alerta de origen activa (RN-RE-20). Al reanudar se
    /// revisan los hitos que vencieron durante la suspensión.
    /// </summary>
    public void Reanudar(DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-20", "Solo se reanuda un proyecto Suspendido.", EstadoProyecto.Suspendido);
        Validar.Regla(!TieneAlertaActiva(TipoAlerta.OrigenModificado), "RN-RE-20",
            "No se reanuda con una alerta de origen modificado activa.");
        CambiarEstado(EstadoProyecto.EnEjecucion, null, ahora);
        GenerarAlertasDeHitosVencidos(DateOnly.FromDateTime(ahora.Date), ahora);
    }

    /// <summary>
    /// Aprobado, EnEjecucion o Suspendido a Cancelado, con motivo (RN-RE-13). Un cambio
    /// Solicitado queda Anulado (RN-RE-09); el caso de uso libera las necesidades.
    /// </summary>
    public void Cancelar(string motivo, DateTimeOffset ahora)
    {
        motivo = Validar.Texto(motivo, "El motivo de la cancelación");
        ExigirEstado("RN-RE-13", "Solo se cancela un proyecto Aprobado, EnEjecucion o Suspendido.",
            EstadoProyecto.Aprobado, EstadoProyecto.EnEjecucion, EstadoProyecto.Suspendido);
        CambiarEstado(EstadoProyecto.Cancelado, motivo, ahora);
        AnularCambioSolicitado("El proyecto pasó a un estado terminal.", ahora);
    }

    /// <summary>
    /// EnEjecucion a Finalizado (RN-RE-12): hitos al 100 %, costo ejecutado dentro del
    /// presupuesto, sin cambio Solicitado, sin alerta de origen activa y con acta de entrega.
    /// </summary>
    public void Finalizar(DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-12", "Solo se finaliza un proyecto EnEjecucion.", EstadoProyecto.EnEjecucion);
        Validar.Regla(_hitos.Count > 0 && _hitos.All(h => h.EstaCompleto), "RN-RE-12", "Todos los hitos deben estar al 100 %.");
        Validar.Regla(CostoEjecutado <= Presupuesto, "RN-RE-12", "El costo ejecutado supera el presupuesto vigente.");
        Validar.Regla(CambioSolicitado is null, "RN-RE-12", "Hay un cambio presupuestal Solicitado.");
        Validar.Regla(!TieneAlertaActiva(TipoAlerta.OrigenModificado), "RN-RE-12", "Hay una alerta de origen modificado activa.");
        Validar.Regla(_documentos.Any(d => d.Tipo == TipoDocumento.ActaEntrega), "RN-RE-12", "Falta el acta de entrega.");

        CambiarEstado(EstadoProyecto.Finalizado, null, ahora);
    }

    // ------------------------------------------------------------------------
    // Gestión operativa
    // ------------------------------------------------------------------------

    /// <summary>Reprograma las fechas de un hito con motivo; los pesos quedan fijos (RN-RE-07).</summary>
    public void ReprogramarHito(Guid hitoId, DateOnly inicio, DateOnly fin, string motivo, DateTimeOffset ahora)
    {
        Validar.Texto(motivo, "El motivo de la reprogramación");
        ExigirEstado("RN-RE-07", "Solo se reprograma un hito en EnEjecucion o Suspendido.",
            EstadoProyecto.EnEjecucion, EstadoProyecto.Suspendido);
        var hito = ObtenerHito(hitoId);
        Validar.Regla(inicio <= fin, "RN-RE-07", "La fecha de inicio no puede ser posterior a la de fin.");
        Validar.Regla(inicio >= FechaInicio && fin <= FechaFin, "RN-RE-07", "El hito queda fuera del rango del cronograma.");

        IncrementarVersion();
        hito.Reprogramar(inicio, fin);
        Registrar(new HitoReprogramado(DesastreId, Id, Version, ahora, hitoId));

        // Hito vencido se resuelve cuando el hito se reprograma (RN-RE-21).
        ResolverAlertaAutomatica(TipoAlerta.HitoVencido, hitoId, ahora);
    }

    /// <summary>
    /// Registra el avance de un hito, solo en EnEjecucion, con fecha efectiva no futura y
    /// porcentaje entre 0 y 100. No disminuye salvo corrección con motivo (RN-RE-10, RN-RE-17).
    /// </summary>
    public Avance RegistrarAvance(Guid hitoId, decimal porcentaje, DateOnly fechaEfectiva,
        IReadOnlyList<ReferenciaEvidencia> evidencias, string? motivoCorreccion, Guid avanceId, DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-10", "El avance se registra solo en EnEjecucion.", EstadoProyecto.EnEjecucion);
        Validar.Porcentaje(porcentaje, "El porcentaje de avance");
        Validar.Regla(fechaEfectiva <= DateOnly.FromDateTime(ahora.Date), "RN-RE-10", "La fecha efectiva no puede ser futura.");
        var hito = ObtenerHito(hitoId);
        if (porcentaje < hito.Avance)
        {
            Validar.Regla(!string.IsNullOrWhiteSpace(motivoCorreccion), "RN-RE-10",
                "El avance no disminuye salvo una corrección con motivo.");
        }

        var evidenciasValidadas = evidencias.Select(e => new ReferenciaEvidencia(
            Validar.Texto(e.Identificador, "El identificador de la evidencia"),
            Validar.Texto(e.Tipo, "El tipo de la evidencia"),
            Validar.Texto(e.Ubicacion, "La ubicación de la evidencia"))).ToList();

        IncrementarVersion();
        var avance = new Avance(avanceId, hitoId, porcentaje, hito.Avance, fechaEfectiva, ahora, evidenciasValidadas,
            string.IsNullOrWhiteSpace(motivoCorreccion) ? null : motivoCorreccion.Trim());
        _avances.Add(avance);
        hito.ActualizarAvance(porcentaje);
        Registrar(new AvanceRegistrado(DesastreId, Id, Version, ahora, hitoId, porcentaje, AvanceTotal));

        if (hito.EstaCompleto)
        {
            ResolverAlertaAutomatica(TipoAlerta.HitoVencido, hitoId, ahora); // RN-RE-21
        }

        return avance;
    }

    /// <summary>
    /// Registra un costo en EnEjecucion o Suspendido (RN-RE-11, RN-RE-13). Un costo que supera
    /// el presupuesto se registra igual, pero genera una alerta de sobrecosto. Un reverso indica
    /// el costo revertido y su motivo, y revierte su monto completo.
    /// </summary>
    public Costo RegistrarCosto(decimal monto, string concepto, DateOnly fechaEfectiva, Guid? costoRevertidoId,
        string? motivoReverso, Guid costoId, DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-13", "Los costos se registran en EnEjecucion o Suspendido.",
            EstadoProyecto.EnEjecucion, EstadoProyecto.Suspendido);
        concepto = Validar.Texto(concepto, "El concepto del costo");

        Costo costo;
        if (costoRevertidoId is { } revertidoId)
        {
            var original = _costos.FirstOrDefault(c => c.Id == revertidoId)
                ?? throw new ElementoNoEncontradoException($"El costo {revertidoId} no existe en el proyecto.");
            Validar.Regla(!original.EsReverso, "RN-RE-11", "Un reverso no se puede revertir.");
            Validar.Regla(_costos.All(c => c.CostoRevertidoId != revertidoId), "RN-RE-11", "El costo ya fue revertido.");
            var motivo = Validar.Texto(motivoReverso, "El motivo del reverso");
            costo = new Costo(costoId, original.Monto, concepto, fechaEfectiva, ahora, revertidoId, motivo);
        }
        else
        {
            Validar.Monto(monto, "El monto del costo");
            costo = new Costo(costoId, monto, concepto, fechaEfectiva, ahora, null, null);
        }

        IncrementarVersion();
        _costos.Add(costo);
        Registrar(new CostoRegistrado(DesastreId, Id, Version, ahora, costo.Id, costo.EsReverso));
        RevisarSobrecosto(ahora);
        return costo;
    }

    /// <summary>
    /// Adjunta la referencia a un documento. En estados terminales solo se admiten
    /// documentos administrativos (RN-RE-14).
    /// </summary>
    public Documento AdjuntarDocumento(TipoDocumento tipo, string nombre, string ubicacion, Guid documentoId, DateTimeOffset ahora)
    {
        nombre = Validar.Texto(nombre, "El nombre del documento");
        ubicacion = Validar.Texto(ubicacion, "La ubicación del documento");
        if (EsTerminal)
        {
            Validar.Regla(tipo == TipoDocumento.Administrativo, "RN-RE-14",
                "En un estado terminal solo se adjuntan documentos administrativos.");
        }

        IncrementarVersion();
        var documento = new Documento(documentoId, tipo, nombre, ubicacion, ahora);
        _documentos.Add(documento);
        Registrar(new DocumentoAdjuntado(DesastreId, Id, Version, ahora, documento.Id, tipo));
        return documento;
    }

    // ------------------------------------------------------------------------
    // Cambios presupuestales (RN-RE-08, RN-RE-09)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Solicita un cambio de presupuesto desde Aprobado. No altera el presupuesto vigente y
    /// solo puede haber uno Solicitado a la vez.
    /// </summary>
    public CambioPresupuestal SolicitarCambio(decimal nuevoPresupuesto, IReadOnlyList<Aporte> nuevosAportes, string motivo,
        Guid solicitanteId, Guid cambioId, DateTimeOffset ahora)
    {
        ExigirEstado("RN-RE-08", "Los cambios de presupuesto se solicitan en Aprobado, EnEjecucion o Suspendido.",
            EstadoProyecto.Aprobado, EstadoProyecto.EnEjecucion, EstadoProyecto.Suspendido);
        Validar.Regla(CambioSolicitado is null, "RN-RE-09", "Ya hay un cambio presupuestal Solicitado.");
        motivo = Validar.Texto(motivo, "El motivo del cambio");
        Validar.Monto(nuevoPresupuesto, "El nuevo presupuesto");
        var aportes = ValidarAportes(nuevosAportes);
        Validar.Regla(aportes.Sum(a => a.Monto) == nuevoPresupuesto, "RN-RE-08",
            "El ajuste de aportes debe sumar exactamente el nuevo presupuesto (RN-RE-06).");

        IncrementarVersion();
        var cambio = new CambioPresupuestal(cambioId, Presupuesto, nuevoPresupuesto, aportes, VersionPresupuesto,
            solicitanteId, motivo, ahora);
        _cambios.Add(cambio);
        Registrar(new CambioPresupuestalSolicitado(DesastreId, Id, Version, ahora, cambio.Id));
        return cambio;
    }

    /// <summary>
    /// Aprueba o rechaza un cambio Solicitado. Lo resuelve una Autoridad distinta del
    /// solicitante, y para aprobar, la versión del presupuesto debe seguir vigente (RN-RE-08).
    /// </summary>
    public void ResolverCambio(Guid cambioId, bool aprobar, string? motivo, Guid resolutorId, DateTimeOffset ahora)
    {
        var cambio = ObtenerCambio(cambioId);
        Validar.Regla(cambio.Estado == EstadoCambioPresupuestal.Solicitado, "RN-RE-08", "El cambio no está Solicitado.");
        Validar.Regla(resolutorId != cambio.SolicitanteId, "RN-RE-08", "El cambio lo resuelve una Autoridad distinta del solicitante.");

        if (!aprobar)
        {
            var motivoRechazo = Validar.Texto(motivo, "El motivo del rechazo");
            IncrementarVersion();
            cambio.Resolver(EstadoCambioPresupuestal.Rechazado, resolutorId, motivoRechazo, ahora);
            Registrar(new CambioPresupuestalResuelto(DesastreId, Id, Version, ahora, cambio.Id, cambio.Estado));
            return;
        }

        Validar.Regla(cambio.VersionPresupuestoBase == VersionPresupuesto, "RN-RE-08",
            "El presupuesto cambió desde que se pidió el cambio.");

        IncrementarVersion();
        cambio.Resolver(EstadoCambioPresupuestal.Aprobado, resolutorId, motivo?.Trim(), ahora);
        Presupuesto = cambio.PresupuestoNuevo;
        VersionPresupuesto++;
        _aportes.Clear();
        // Copias: el cambio conserva sus aportes como historial (RN-RE-08) y cada instancia
        // owned debe pertenecer a un solo dueño en la persistencia.
        _aportes.AddRange(cambio.NuevosAportes.Select(a => a with { }));
        Registrar(new CambioPresupuestalResuelto(DesastreId, Id, Version, ahora, cambio.Id, cambio.Estado));
        RevisarSobrecosto(ahora);
    }

    /// <summary>El solicitante retira su cambio Solicitado; queda Anulado (RN-RE-09).</summary>
    public void RetirarCambio(Guid cambioId, string motivo, Guid solicitanteId, DateTimeOffset ahora)
    {
        motivo = Validar.Texto(motivo, "El motivo del retiro");
        var cambio = ObtenerCambio(cambioId);
        Validar.Regla(cambio.Estado == EstadoCambioPresupuestal.Solicitado, "RN-RE-09", "El cambio no está Solicitado.");
        Validar.Regla(cambio.SolicitanteId == solicitanteId, "RN-RE-09", "Solo el solicitante puede retirar el cambio.");

        IncrementarVersion();
        cambio.Resolver(EstadoCambioPresupuestal.Anulado, null, motivo, ahora);
        Registrar(new CambioPresupuestalResuelto(DesastreId, Id, Version, ahora, cambio.Id, cambio.Estado));
    }

    // ------------------------------------------------------------------------
    // Alertas (RN-RE-20, RN-RE-21)
    // ------------------------------------------------------------------------

    /// <summary>
    /// Caracterización corrigió o revocó la evaluación de una necesidad asignada a este proyecto
    /// (RN-RE-20). Mientras la alerta esté activa, el proyecto no se finaliza ni se reanuda.
    /// </summary>
    public void RegistrarAlertaOrigen(Guid necesidadId, DateTimeOffset ahora)
    {
        Validar.Regla(_necesidadIds.Contains(necesidadId), "RN-RE-20", "La necesidad no pertenece al proyecto.");
        if (EsTerminal || TieneAlertaActivaSobre(TipoAlerta.OrigenModificado, necesidadId))
        {
            return; // En terminales se registra como rectificación (RN-RE-14); una sola alerta activa (RN-RE-21).
        }

        IncrementarVersion();
        GenerarAlerta(TipoAlerta.OrigenModificado, necesidadId,
            $"La evaluación de la necesidad {necesidadId} fue modificada en Caracterización.", ahora);
    }

    /// <summary>La Autoridad resuelve una alerta de origen modificado con justificación (RN-RE-21).</summary>
    public void ResolverAlerta(Guid alertaId, string justificacion, DateTimeOffset ahora)
    {
        justificacion = Validar.Texto(justificacion, "La justificación");
        var alerta = _alertas.FirstOrDefault(a => a.Id == alertaId)
            ?? throw new ElementoNoEncontradoException($"La alerta {alertaId} no existe en el proyecto.");
        Validar.Regla(alerta.EstaActiva, "RN-RE-21", "La alerta ya está resuelta.");
        Validar.Regla(alerta.Tipo == TipoAlerta.OrigenModificado, "RN-RE-21",
            "Las alertas de sobrecosto y de hito vencido se resuelven solas al corregirse su causa.");

        IncrementarVersion();
        alerta.Resolver(ahora, justificacion);
        Registrar(new AlertaResuelta(DesastreId, Id, Version, ahora, alerta.Id, alerta.Tipo));
    }

    /// <summary>
    /// VigilanciaCronograma: genera alertas de hitos vencidos sin completar en proyectos
    /// EnEjecucion, sin cambiar el estado (RN-RE-21). Devuelve cuántas generó.
    /// </summary>
    public int RevisarHitosVencidos(DateTimeOffset ahora)
    {
        if (Estado != EstadoProyecto.EnEjecucion)
        {
            return 0;
        }

        var vencidos = HitosVencidosSinAlerta(DateOnly.FromDateTime(ahora.Date));
        if (vencidos.Count == 0)
        {
            return 0;
        }

        IncrementarVersion();
        foreach (var hito in vencidos)
        {
            GenerarAlerta(TipoAlerta.HitoVencido, hito.Id, $"El hito {hito.Nombre} venció sin completarse.", ahora);
        }

        return vencidos.Count;
    }

    public bool TieneAlertaActiva(TipoAlerta tipo) => _alertas.Any(a => a.Tipo == tipo && a.EstaActiva);

    // ------------------------------------------------------------------------
    // Reglas internas
    // ------------------------------------------------------------------------

    private void CambiarEstado(EstadoProyecto nuevo, string? motivo, DateTimeOffset ahora)
    {
        var anterior = Estado;
        IncrementarVersion();
        Estado = nuevo;
        if (motivo is not null)
        {
            UltimoMotivo = motivo;
        }

        Registrar(new ProyectoEstadoCambiado(DesastreId, Id, Version, ahora, anterior, nuevo, motivo));
    }

    /// <summary>RN-RE-11 y RN-RE-21: genera o resuelve la alerta de sobrecosto según el costo ejecutado.</summary>
    private void RevisarSobrecosto(DateTimeOffset ahora)
    {
        var hayExceso = CostoEjecutado > Presupuesto;
        var activa = TieneAlertaActivaSobre(TipoAlerta.Sobrecosto, Id);

        if (hayExceso && !activa)
        {
            GenerarAlerta(TipoAlerta.Sobrecosto, Id, "El costo ejecutado supera el presupuesto vigente.", ahora);
        }
        else if (!hayExceso && activa)
        {
            ResolverAlertaAutomatica(TipoAlerta.Sobrecosto, Id, ahora);
        }
    }

    private void GenerarAlertasDeHitosVencidos(DateOnly hoy, DateTimeOffset ahora)
    {
        foreach (var hito in HitosVencidosSinAlerta(hoy))
        {
            GenerarAlerta(TipoAlerta.HitoVencido, hito.Id, $"El hito {hito.Nombre} venció sin completarse.", ahora);
        }
    }

    private List<Hito> HitosVencidosSinAlerta(DateOnly hoy) =>
        _hitos.Where(h => h.FechaFin < hoy && !h.EstaCompleto && !TieneAlertaActivaSobre(TipoAlerta.HitoVencido, h.Id))
            .ToList();

    /// <summary>Debe llamarse después de incrementar la versión en la misma operación.</summary>
    private void GenerarAlerta(TipoAlerta tipo, Guid objetoId, string descripcion, DateTimeOffset ahora)
    {
        var alerta = new Alerta(Guid.NewGuid(), tipo, objetoId, descripcion, ahora);
        _alertas.Add(alerta);
        Registrar(new AlertaGenerada(DesastreId, Id, Version, ahora, alerta.Id, tipo, objetoId));
    }

    private void ResolverAlertaAutomatica(TipoAlerta tipo, Guid objetoId, DateTimeOffset ahora)
    {
        var alerta = _alertas.FirstOrDefault(a => a.Tipo == tipo && a.ObjetoId == objetoId && a.EstaActiva);
        if (alerta is null)
        {
            return;
        }

        alerta.Resolver(ahora, null);
        Registrar(new AlertaResuelta(DesastreId, Id, Version, ahora, alerta.Id, tipo));
    }

    private bool TieneAlertaActivaSobre(TipoAlerta tipo, Guid objetoId) =>
        _alertas.Any(a => a.Tipo == tipo && a.ObjetoId == objetoId && a.EstaActiva);

    private void AnularCambioSolicitado(string motivo, DateTimeOffset ahora)
    {
        var cambio = CambioSolicitado;
        if (cambio is null)
        {
            return;
        }

        cambio.Resolver(EstadoCambioPresupuestal.Anulado, null, motivo, ahora);
        Registrar(new CambioPresupuestalResuelto(DesastreId, Id, Version, ahora, cambio.Id, cambio.Estado));
    }

    private static List<Aporte> ValidarAportes(IReadOnlyList<Aporte> aportes)
    {
        var validados = aportes
            .Select(a => new Aporte(Validar.Texto(a.Fuente, "La fuente del aporte"), Validar.Monto(a.Monto, "El monto del aporte")))
            .ToList();
        Validar.Regla(validados.Select(a => a.Fuente.ToUpperInvariant()).Distinct().Count() == validados.Count, "RN-RE-06",
            "Cada aporte debe identificar una fuente distinta.");
        return validados;
    }

    private void ExigirEstado(string regla, string mensaje, params EstadoProyecto[] permitidos) =>
        Validar.Regla(permitidos.Contains(Estado), regla, $"{mensaje} Estado actual: {Estado}.");

    private void ExigirNoTerminal(string regla) =>
        Validar.Regla(!EsTerminal, regla, $"El proyecto está en un estado terminal ({Estado}).");

    private Hito ObtenerHito(Guid hitoId) =>
        _hitos.FirstOrDefault(h => h.Id == hitoId)
        ?? throw new ElementoNoEncontradoException($"El hito {hitoId} no existe en el proyecto.");

    private CambioPresupuestal ObtenerCambio(Guid cambioId) =>
        _cambios.FirstOrDefault(c => c.Id == cambioId)
        ?? throw new ElementoNoEncontradoException($"El cambio presupuestal {cambioId} no existe en el proyecto.");
}
