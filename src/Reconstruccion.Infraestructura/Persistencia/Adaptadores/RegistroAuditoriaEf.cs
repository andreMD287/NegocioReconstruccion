using Microsoft.EntityFrameworkCore;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Infraestructura.Persistencia.Registros;

namespace Reconstruccion.Infraestructura.Persistencia.Adaptadores;

public sealed class RegistroAuditoriaEf(
    ReconstruccionDbContext db,
    IDbContextFactory<ReconstruccionDbContext> fabrica)
    : IRegistroAuditoria
{
    // No guarda inmediatamente: participa en la unidad de trabajo.
    public void Registrar(EntradaAuditoria entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        db.Auditorias.Add(new AuditoriaPersistida(entrada));
    }

    // Un rechazo debe sobrevivir al rollback de la operacion principal.
    public async Task RegistrarRechazoAsync(
        EntradaAuditoria entrada,
        CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (entrada.Resultado != ResultadoOperacion.Rechazado)
        {
            throw new ArgumentException(
                "La auditoria independiente requiere un resultado Rechazado.",
                nameof(entrada));
        }

        // IMPORTANTE: La fabrica debe configurarse con una cadena
        // de conexion, no reutilizando una conexion/transaccion externa.
        await using var independiente =
            await fabrica.CreateDbContextAsync(cancelacion);

        independiente.Auditorias.Add(
            new AuditoriaPersistida(entrada));

        await independiente.SaveChangesAsync(cancelacion);
    }
}