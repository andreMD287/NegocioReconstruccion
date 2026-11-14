using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Integracion;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia.Adaptadores;

namespace Reconstruccion.Infraestructura.Persistencia.Publicacion;

/// <summary>
/// Permite probar la entrega sin depender de SignalR o Cierre reales.
/// </summary>
public interface IEntregaEventoOutbox
{
    Task<ResultadoDistribucion> EntregarAsync(
        IEventoDominio evento,
        CancellationToken cancelacion = default);
}

/// <summary>
/// Conecta el procesador con el distribuidor de Aplicacion.
/// </summary>
public sealed class EntregaEventoOutbox(
    DistribuidorEventos distribuidor) : IEntregaEventoOutbox
{
    public Task<ResultadoDistribucion> EntregarAsync(
        IEventoDominio evento,
        CancellationToken cancelacion = default) =>
        distribuidor.DistribuirAsync(evento, cancelacion);
}

/// <summary>
/// Resultado de una ejecucion del procesador.
/// </summary>
public sealed record ResultadoLoteOutbox(
    int DesastresRevisados,
    int EventosPublicados,
    int EventosFallidos);

/// <summary>
/// Procesa los eventos pendientes por desastre y secuencia.
///
/// Si un evento falla, no procesa los siguientes eventos del
/// mismo desastre durante esta ejecucion.
///
/// Un desastre fallido no bloquea los demas desastres.
/// </summary>
public sealed class ProcesadorLoteOutbox(
    ReconstruccionDbContext db,
    IEntregaEventoOutbox entrega)
{
    public async Task<ResultadoLoteOutbox> ProcesarAsync(
        int maxEventosPorDesastre = 25,
        CancellationToken cancelacion = default)
    {
        if (maxEventosPorDesastre <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxEventosPorDesastre));
        }

        // Cada desastre se procesa de forma independiente.
        // No se limita la cantidad de desastres: uno con errores
        // no debe impedir que otros tengan su oportunidad.
        var desastres = await db.EventosOutbox
            .AsNoTracking()
            .Where(e => e.PublicadoEn == null)
            .Select(e => e.DesastreId)
            .Distinct()
            .OrderBy(id => id)
            .ToListAsync(cancelacion);

        var publicados = 0;
        var fallidos = 0;

        foreach (var desastreId in desastres)
        {
            cancelacion.ThrowIfCancellationRequested();

            for (var numero = 0;
                 numero < maxEventosPorDesastre;
                 numero++)
            {
                // Siempre consultar el primer evento pendiente
                // por Secuencia, nunca por VersionAgregado.
                var pendiente = await db.EventosOutbox
                    .Where(e =>
                        e.DesastreId == desastreId &&
                        e.PublicadoEn == null)
                    .OrderBy(e => e.Secuencia)
                    .FirstOrDefaultAsync(cancelacion);

                if (pendiente is null)
                {
                    break;
                }

                var fallo = false;

                try
                {
                    var evento =
                        SerializadorEventosOutbox.Deserializar(
                            pendiente.TipoEvento,
                            pendiente.ContenidoJson);

                    var resultado = await entrega.EntregarAsync(
                        evento,
                        cancelacion);

                    if (resultado.Entregado)
                    {
                        pendiente.MarcarPublicado(
                            DateTimeOffset.UtcNow);

                        publicados++;
                    }
                    else
                    {
                        pendiente.RegistrarIntentoFallido(
                            resultado.Motivo ??
                            "La distribucion solicito reintentar.");

                        fallidos++;
                        fallo = true;
                    }
                }
                catch (OperationCanceledException)
                    when (cancelacion.IsCancellationRequested)
                {
                    // Una parada del servicio no debe
                    // registrarse como intento fallido.
                    throw;
                }
                catch (Exception ex)
                {
                    pendiente.RegistrarIntentoFallido(
                        $"{ex.GetType().Name}: {ex.Message}");

                    fallidos++;
                    fallo = true;
                }

                // Confirmar el resultado de cada evento.
                // Si falla la escritura, la excepcion se propaga:
                // el evento no se considera publicado.
                await db.SaveChangesAsync(cancelacion);

                // Evitar retener innecesariamente eventos
                // procesados durante lotes extensos.
                db.Entry(pendiente).State =
                    EntityState.Detached;

                if (fallo)
                {
                    // No enviar los eventos posteriores
                    // de este desastre en esta vuelta.
                    break;
                }
            }
        }

        return new ResultadoLoteOutbox(
            desastres.Count,
            publicados,
            fallidos);
    }
}