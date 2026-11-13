using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Infraestructura.Persistencia.Adaptadores;

/// <summary>
/// Confirma todos los cambios pendientes del DbContext compartido:
/// agregados, auditoria, solicitudes procesadas y outbox.
///
/// Los adaptadores deben utilizar la misma instancia de DbContext.
/// SaveChangesAsync es transaccional con PostgreSQL.
///
/// Los rechazos de negocio se registran independientemente
/// mediante IRegistroAuditoria.RegistrarRechazoAsync.
/// </summary>
public sealed class UnidadDeTrabajoEf(
    ReconstruccionDbContext db) : IUnidadDeTrabajo
{
    public async Task ConfirmarAsync(
        CancellationToken cancelacion = default)
    {
        try
        {
            await db.SaveChangesAsync(cancelacion);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictoDeVersionException(
                "El Plan o Proyecto fue modificado por otra operacion. " +
                "Es necesario consultar su version actual antes de reintentar.",
                ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: "IX_planes_reconstruccion_DesastreId"
                    or "UX_necesidades_PlanId_evaluacion_id"
            })
        {
            throw new ConflictoDeVersionException(
                "Otro cambio registro la misma informacion antes. " +
                "Consulta el estado actualizado antes de reintentar.",
                ex);
        }
    }
}