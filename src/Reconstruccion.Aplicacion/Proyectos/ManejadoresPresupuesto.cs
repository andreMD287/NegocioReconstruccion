using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Aplicacion.Proyectos;

// Cambios presupuestales (RN-RE-08, RN-RE-09). El historial nunca se sobrescribe: cada
// solicitud, resolución y retiro queda auditado con el valor anterior y el nuevo (RN-RE-16).

/// <summary>Solicita un cambio de presupuesto; Responsable asignado o Autoridad.</summary>
public sealed class SolicitarCambioPresupuestoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<SolicitarCambioPresupuesto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(SolicitarCambioPresupuesto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirOperadorOAutoridad(ctx.Usuario, proyecto);

            var cambio = proyecto.SolicitarCambio(comando.NuevoPresupuesto, Carga.Aportes(comando.NuevosAportes), comando.Motivo,
                ctx.Usuario.UsuarioId, Guid.CreateVersion7(), ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId,
                $"Cambio {cambio.Id} solicitado: {cambio.PresupuestoAnterior} -> {cambio.PresupuestoNuevo}. Motivo: {cambio.Motivo}");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>Aprueba o rechaza un cambio Solicitado; Autoridad distinta del solicitante (RN-RE-08).</summary>
public sealed class ResolverCambioPresupuestoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<ResolverCambioPresupuesto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(ResolverCambioPresupuesto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Autoridad);
            var presupuestoAnterior = proyecto.Presupuesto;

            proyecto.ResolverCambio(comando.CambioId, comando.Aprobar, comando.Motivo, ctx.Usuario.UsuarioId, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, comando.Aprobar
                ? $"Cambio {comando.CambioId} aprobado: presupuesto {presupuestoAnterior} -> {proyecto.Presupuesto}."
                : $"Cambio {comando.CambioId} rechazado. Motivo: {comando.Motivo}");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}

/// <summary>El solicitante retira su cambio Solicitado; queda Anulado (RN-RE-09).</summary>
public sealed class RetirarCambioPresupuestoManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<RetirarCambioPresupuesto, ResultadoComando>
{
    public Task<Resultado<ResultadoComando>> ManejarAsync(RetirarCambioPresupuesto comando, CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            var proyecto = await Carga.ProyectoAsync(proyectos, comando.ProyectoId, comando.ExpectedVersion, ctx, cancelacion);
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Responsable, Rol.Autoridad);

            // El dominio verifica que quien retira sea el solicitante (RN-RE-09).
            proyecto.RetirarCambio(comando.CambioId, comando.Motivo, ctx.Usuario.UsuarioId, ctx.Ahora);

            ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"Cambio {comando.CambioId} retirado. Motivo: {comando.Motivo}");
            return new ResultadoComando(proyecto.Id, proyecto.Version);
        }, cancelacion);
}
