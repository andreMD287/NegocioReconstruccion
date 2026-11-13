using System.Text.Json;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Infraestructura.Persistencia.Adaptadores;

/// <summary>
/// Serializa eventos de dominio con un discriminador conocido.
/// No utiliza Type.GetType sobre contenido procedente de la base.
/// </summary>
public static class SerializadorEventosOutbox
{
    private static readonly JsonSerializerOptions Opciones =
        new(JsonSerializerDefaults.Web);

    private static readonly IReadOnlyDictionary<string, Type> Tipos =
        new Dictionary<string, Type>(StringComparer.Ordinal)
        {
            [nameof(NecesidadesRecibidas)] = typeof(NecesidadesRecibidas),
            [nameof(NecesidadDescartada)] = typeof(NecesidadDescartada),
            [nameof(NecesidadReactivada)] = typeof(NecesidadReactivada),

            [nameof(PlanCompletado)] = typeof(PlanCompletado),
            [nameof(PlanReabierto)] = typeof(PlanReabierto),
            [nameof(PlanCerrado)] = typeof(PlanCerrado),

            [nameof(ProyectoCreado)] = typeof(ProyectoCreado),
            [nameof(ParticipantesAsignados)] = typeof(ParticipantesAsignados),
            [nameof(ProyectoEstadoCambiado)] = typeof(ProyectoEstadoCambiado),

            [nameof(AvanceRegistrado)] = typeof(AvanceRegistrado),
            [nameof(CostoRegistrado)] = typeof(CostoRegistrado),
            [nameof(HitoReprogramado)] = typeof(HitoReprogramado),

            [nameof(DocumentoAdjuntado)] = typeof(DocumentoAdjuntado),

            [nameof(CambioPresupuestalSolicitado)] =
                typeof(CambioPresupuestalSolicitado),

            [nameof(CambioPresupuestalResuelto)] =
                typeof(CambioPresupuestalResuelto),

            [nameof(AlertaGenerada)] = typeof(AlertaGenerada),
            [nameof(AlertaResuelta)] = typeof(AlertaResuelta)
        };

    public static (string Tipo, string Json) Serializar(
        IEventoDominio evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var tipo = evento.GetType();

        if (!Tipos.TryGetValue(tipo.Name, out var permitido) ||
            permitido != tipo)
        {
            throw new NotSupportedException(
                $"El evento {tipo.FullName} no esta registrado en el catalogo.");
        }

        var json = JsonSerializer.Serialize(
            evento,
            tipo,
            Opciones);

        return (tipo.Name, json);
    }

    public static IEventoDominio Deserializar(
        string tipoEvento,
        string contenidoJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tipoEvento);
        ArgumentException.ThrowIfNullOrWhiteSpace(contenidoJson);

        if (!Tipos.TryGetValue(tipoEvento, out var tipo))
        {
            throw new NotSupportedException(
                $"Tipo de evento desconocido: {tipoEvento}");
        }

        var resultado = JsonSerializer.Deserialize(
            contenidoJson,
            tipo,
            Opciones);

        if (resultado is not IEventoDominio evento)
        {
            throw new JsonException(
                $"No fue posible reconstruir el evento {tipoEvento}.");
        }

        return evento;
    }
}