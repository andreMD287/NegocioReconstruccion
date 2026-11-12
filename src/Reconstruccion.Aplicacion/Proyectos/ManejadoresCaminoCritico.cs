using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Aplicacion.Proyectos;

// Casos de uso del camino crítico de la demostración: crear, preparar, aprobar, iniciar,
// registrar avance, costo y documentos, y finalizar. Cada uno verifica la autorización
// contextual (RN-RE-19) y la versión esperada (RN-RE-18); el EjecutorOperaciones se
// encarga de la idempotencia, la auditoría, el outbox y los errores.

internal static class Carga
{
    public static async Task<ProyectoReconstruccion> ProyectoAsync(IRepositorioProyectos proyectos, Guid proyectoId,
        long versionEsperada, ContextoOperacion ctx, CancellationToken cancelacion)
    {
        var proyecto = await proyectos.ObtenerPorIdAsync(proyectoId, cancelacion)
            ?? throw new NoEncontradoException($"El proyecto {proyectoId} no existe.");
        ctx.FijarObjetivo("Proyecto", proyecto.Id, proyecto.DesastreId);
        ctx.VerificarVersion(proyecto, versionEsperada, "el proyecto");
        return ctx.Seguir(proyecto);
    }

    public static async Task<PlanReconstruccion> PlanAsync(IRepositorioPlanes planes, Guid desastreId,
        long versionEsperada, ContextoOperacion ctx, CancellationToken cancelacion)
    {
        var plan = await planes.ObtenerPorDesastreAsync(desastreId, cancelacion)
            ?? throw new NoEncontradoException($"No existe plan para el desastre {desastreId}.");
        ctx.VerificarVersion(plan, versionEsperada, "el plan");
        return ctx.Seguir(plan);
    }

    public static List<Aporte> Aportes(IEnumerable<AporteDto> aportes) =>
        aportes.Select(a => new Aporte(a.Fuente, a.Monto)).ToList();
}

/// <summary>
/// CrearProyecto (RN-RE-01, 03, 05, 18 y 19). Modifica el plan y el proyecto en una sola
/// unidad de trabajo (sección 7); quien llama queda como formulador.
/// </summary>
public sealed class CrearProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes, IRepositorioProyectos proyectos)
    : IManejadorComando<CrearProyecto, ResultadoComandoConPlan>
{
    public Task<Resultado<ResultadoComandoConPlan>> ManejarAsync(CrearProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            ctx.FijarObjetivo("Plan", Guid.Empty, comando.DesastreId);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Responsable, Rol.Autoridad);
            var plan = await Carga.PlanAsync(planes, comando.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);

            var proyectoId = Guid.CreateVersion7();
            plan.AsignarNecesidades(comando.NecesidadIds, proyectoId);
            var proyecto = ProyectoReconstruccion.Proponer(proyectoId, comando.DesastreId, ctx.Usuario.UsuarioId,
                comando.Nombre, comando.Descripcion, comando.Tipo, comando.NecesidadIds, ctx.Ahora);
            proyectos.Agregar(ctx.Seguir(proyecto));

            ctx.FijarObjetivo("Proyecto", proyecto.Id, proyecto.DesastreId);
            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Creado en Propuesto con {comando.NecesidadIds.Count} necesidades.");
            return new ResultadoComandoConPlan(proyecto.Id, proyecto.Version, plan.Version);
        }, cancelacion);
}

/// <summary>Edición directa de presupuesto y aportes en Propuesto, por el formulador (RN-RE-08, RN-RE-19).</summary>
public sealed class EditarPresupuestoPropuestoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<EditarPresupuestoPropuesto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(EditarPresupuestoPropuesto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);
            var anterior = proyecto.Presupuesto;

            proyecto.EditarPresupuesto(comando.Presupuesto, Carga.Aportes(comando.Aportes));

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Presupuesto propuesto: {anterior} -> {proyecto.Presupuesto}; {proyecto.Aportes.Count} aportes.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Asigna responsable y/o contratista; rol Autoridad (sección 4).</summary>
public sealed class AsignarParticipantesManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<AsignarParticipantes, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(AsignarParticipantes comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);

            proyecto.AsignarParticipantes(
                comando.Responsable is { } r ? new Responsable(r.UsuarioId, r.OrganismoId) : null,
                comando.Contratista is { } c ? new Contratista(c.Identificacion, c.Nombre) : null,
                ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Responsable: {proyecto.ResponsableId?.ToString() ?? "sin asignar"}; contratista: {proyecto.Contratista?.Nombre ?? "sin asignar"}.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Define el cronograma en Propuesto (formulador) o Aprobado (Responsable) (RN-RE-07, RN-RE-19).</summary>
public sealed class DefinirCronogramaManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<DefinirCronograma, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(DefinirCronograma comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);

            proyecto.DefinirCronograma(comando.FechaInicio, comando.FechaFin,
                comando.Hitos.Select(h => (h.Nombre, h.FechaInicio, h.FechaFin, h.Peso)).ToList(), Guid.CreateVersion7);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Cronograma {comando.FechaInicio:yyyy-MM-dd} a {comando.FechaFin:yyyy-MM-dd} con {comando.Hitos.Count} hitos.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Propuesto a Aprobado; rol Autoridad distinto del formulador (RN-RE-06).</summary>
public sealed class AprobarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<AprobarProyecto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(AprobarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);

            proyecto.Aprobar(ctx.Usuario.UsuarioId, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Propuesto -> Aprobado con presupuesto {proyecto.Presupuesto} y {proyecto.Aportes.Count} aportes.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Aprobado a EnEjecucion; Responsable asignado (RN-RE-07, RN-RE-19).</summary>
public sealed class IniciarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<IniciarProyecto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(IniciarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);

            proyecto.Iniciar(ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, "Aprobado -> EnEjecucion.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Registra el avance de un hito; Responsable asignado (RN-RE-10, 17, 19 y 21).</summary>
public sealed class RegistrarAvanceManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<RegistrarAvance, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(RegistrarAvance comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);

            var avance = proyecto.RegistrarAvance(comando.HitoId, comando.Porcentaje, comando.FechaEfectiva,
                comando.Evidencias.Select(e => new ReferenciaEvidencia(e.Identificador, e.Tipo, e.Ubicacion)).ToList(),
                comando.MotivoCorreccion, Guid.CreateVersion7(), ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Avance del hito {comando.HitoId}: {avance.PorcentajeAnterior} -> {avance.Porcentaje}; " +
                $"{avance.Evidencias.Count} evidencias; avance del proyecto {proyecto.AvanceTotal}.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Registra un costo o su reverso; Responsable asignado (RN-RE-11, 13, 17, 19 y 21).</summary>
public sealed class RegistrarCostoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<RegistrarCosto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(RegistrarCosto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);

            var costo = proyecto.RegistrarCosto(comando.Monto, comando.Concepto, comando.FechaEfectiva,
                comando.CostoRevertidoId, comando.MotivoReverso, Guid.CreateVersion7(), ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                (costo.EsReverso ? $"Reverso del costo {costo.CostoRevertidoId}" : "Costo") +
                $" por {costo.Monto}; costo ejecutado {proyecto.CostoEjecutado} de {proyecto.Presupuesto}.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Adjunta la referencia a un documento (RN-RE-12, 14 y 19).</summary>
public sealed class AdjuntarDocumentoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<AdjuntarDocumento, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(AdjuntarDocumento comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);

            var documento = proyecto.AdjuntarDocumento(comando.Tipo, comando.Nombre, comando.Ubicacion, Guid.CreateVersion7(), ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"Documento {documento.Tipo}: {documento.Nombre}.");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>
/// EnEjecucion a Finalizado (RN-RE-12) y necesidades a Atendida (RN-RE-03). Modifica proyecto
/// y plan en una sola unidad de trabajo; el plan evalúa su completitud (RN-RE-15).
/// </summary>
public sealed class FinalizarProyectoManejador(EjecutorOperaciones ejecutor, IRepositorioPlanes planes, IRepositorioProyectos proyectos)
    : IManejadorComando<FinalizarProyecto, ResultadoComandoConPlan>
{
    public Task<Resultado<ResultadoComandoConPlan>> ManejarAsync(FinalizarProyecto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperador(ctx.Usuario, proyecto);
            var plan = await Carga.PlanAsync(planes, proyecto.DesastreId, comando.ExpectedVersionPlan, ctx, cancelacion);

            proyecto.Finalizar(ctx.Ahora);
            plan.AtenderNecesidades(proyecto.Id, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"EnEjecucion -> Finalizado; costo ejecutado {proyecto.CostoEjecutado} de {proyecto.Presupuesto}.");
            ctx.Auditar("Plan", plan.Id, plan.DesastreId,
                $"{proyecto.NecesidadIds.Count} necesidades atendidas; plan {plan.Estado}.");
            return new ResultadoComandoConPlan(proyecto.Id, proyecto.Version, plan.Version);
        }, cancelacion);
}
