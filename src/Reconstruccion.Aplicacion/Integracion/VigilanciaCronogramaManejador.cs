using Reconstruccion.Aplicacion.Comun;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Aplicacion.Integracion;

/// <summary>
/// VigilanciaCronograma (sección 4): proceso periódico con rol Sistema que genera alertas de
/// hito vencido en los proyectos EnEjecucion, sin cambiar su estado (RN-RE-21).
/// Revisa todos los proyectos en una sola unidad de trabajo; si otro cambio gana la carrera,
/// la operación falla con conflicto y la siguiente ejecución vuelve a revisar.
/// </summary>
public sealed class VigilanciaCronogramaManejador(EjecutorOperaciones ejecutor, IRepositorioProyectos proyectos)
    : IManejadorComando<EjecutarVigilanciaCronograma, ResultadoVigilanciaCronograma>
{
    public Task<Resultado<ResultadoVigilanciaCronograma>> ManejarAsync(EjecutarVigilanciaCronograma comando,
        CancellationToken cancelacion = default) =>
        ejecutor.EjecutarAsync(comando, async ctx =>
        {
            Autorizacion.ExigirRol(ctx.Usuario, Rol.Sistema);
            var ids = await proyectos.ListarIdsEnEjecucionAsync(cancelacion);
            var alertas = 0;

            foreach (var id in ids)
            {
                var proyecto = await proyectos.ObtenerPorIdAsync(id, cancelacion);
                if (proyecto is null)
                {
                    continue;
                }

                var generadas = proyecto.RevisarHitosVencidos(ctx.Ahora);
                if (generadas == 0)
                {
                    continue;
                }

                ctx.Seguir(proyecto);
                ctx.Auditar("Proyecto", proyecto.Id, proyecto.DesastreId, $"{generadas} alertas de hito vencido.");
                alertas += generadas;
            }

            return new ResultadoVigilanciaCronograma(ids.Count, alertas);
        }, cancelacion);
}
