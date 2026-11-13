using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;
using Reconstruccion.Infraestructura.Persistencia.Registros;

namespace Reconstruccion.Infraestructura.Persistencia.Adaptadores;

/// <summary>
/// Registra los eventos en el DbContext de la operacion.
/// No publica ni confirma transacciones por su cuenta.
/// </summary>
public sealed class OutboxEf(ReconstruccionDbContext db) : IOutbox
{
    public void Agregar(IEnumerable<IEventoDominio> eventos)
    {
        ArgumentNullException.ThrowIfNull(eventos);

        foreach (var evento in eventos)
        {
            ArgumentNullException.ThrowIfNull(evento);

            if (evento.EventoId == Guid.Empty)
            {
                throw new ArgumentException(
                    "El EventoId no puede estar vacio.",
                    nameof(eventos));
            }

            var (tipo, json) =
                SerializadorEventosOutbox.Serializar(evento);

            db.EventosOutbox.Add(
                new EventoOutboxPersistido(
                    evento.EventoId,
                    evento.DesastreId,
                    evento.AgregadoId,
                    evento.VersionAgregado,
                    evento.OcurridoEn,
                    tipo,
                    json));
        }
    }
}