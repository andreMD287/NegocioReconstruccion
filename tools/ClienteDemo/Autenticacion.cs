using System.Net.Http.Json;
using Grpc.Core;

namespace Reconstruccion.ClienteDemo;

/// <summary>
/// Identidad de un usuario de la demostración. El identificador lo genera el cliente, no el
/// endpoint de tokens, para poder referenciarlo luego (RN-RE-19: formulador, Responsable, etc.).
/// </summary>
internal sealed record Usuario(string Rol, Guid Id, string Token)
{
    public Metadata Encabezados() => new() { { "Authorization", $"Bearer {Token}" } };
}

/// <summary>Pide tokens al endpoint <c>/dev/token</c>, que el Host solo expone en Development.</summary>
internal static class Autenticacion
{
    public static async Task<Usuario> ObtenerAsync(HttpClient http, string rol, Guid? usuarioId = null, Guid? organismoId = null)
    {
        var id = usuarioId ?? Guid.NewGuid();
        var respuesta = await http.PostAsJsonAsync("/dev/token", new { rol, usuarioId = id, organismoId });
        if (!respuesta.IsSuccessStatusCode)
        {
            var detalle = await respuesta.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"No se pudo obtener un token de prueba para el rol {rol} ({respuesta.StatusCode}): {detalle}. " +
                "¿El Host está corriendo en Development?");
        }

        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>()
            ?? throw new InvalidOperationException("El Host no devolvió un token.");
        return new Usuario(rol, id, cuerpo.Token);
    }

    private sealed record RespuestaToken(string Token);
}
