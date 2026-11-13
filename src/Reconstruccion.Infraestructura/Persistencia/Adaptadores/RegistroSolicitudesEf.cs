using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Persistencia.Registros;

namespace Reconstruccion.Infraestructura.Persistencia.Adaptadores;

public sealed class RegistroSolicitudesEf(ReconstruccionDbContext db)
    : IRegistroSolicitudes
{
    public async Task<SolicitudProcesada?> ObtenerAsync(
        Guid requestId,
        CancellationToken cancelacion = default)
    {
        var fila = await db.SolicitudesProcesadas
            .AsNoTracking()
            .SingleOrDefaultAsync(
                s => s.RequestId == requestId,
                cancelacion);

        return fila?.ComoContrato();
    }

    // No realiza SaveChanges: participa en la unidad de trabajo.
    public void Registrar(SolicitudProcesada solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        if (solicitud.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "RequestId no puede estar vacio.",
                nameof(solicitud));
        }

        db.SolicitudesProcesadas.Add(
            new SolicitudProcesadaPersistida(solicitud));
    }
}