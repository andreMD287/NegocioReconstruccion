using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Dominio.Comun;

namespace Reconstruccion.Aplicacion.Comun;

/// <summary>
/// Ejecuta un caso de uso con los comportamientos transversales de la sección 2:
/// idempotencia y concurrencia optimista (RN-RE-18), auditoría en la misma transacción y
/// rechazos en escritura independiente (RN-RE-16), eventos al outbox (sección 8.2) y
/// traducción de las excepciones de dominio a <see cref="CodigoError"/>.
/// Cumple el papel del BizRunner de la Clase 17.
/// </summary>
public sealed class EjecutorOperaciones(
    IUnidadDeTrabajo unidadDeTrabajo,
    IRegistroSolicitudes solicitudes,
    IRegistroAuditoria auditoria,
    IOutbox outbox,
    IUsuarioActual usuario,
    IReloj reloj)
{
    private static readonly JsonSerializerOptions OpcionesJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Ejecuta <paramref name="operacion"/>. Si el comando implementa <see cref="IComando"/>, una
    /// solicitud repetida devuelve la respuesta original sin volver a ejecutarse.
    /// </summary>
    public async Task<Resultado<T>> EjecutarAsync<T>(
        object comando,
        Func<ContextoOperacion, Task<T>> operacion,
        CancellationToken cancelacion = default)
    {
        var accion = comando.GetType().Name;
        var requestId = (comando as IComando)?.RequestId;
        string? huella = null;

        if (requestId is { } id)
        {
            if (id == Guid.Empty)
            {
                return Resultado<T>.Fallo(CodigoError.DatosInvalidos, "El RequestId es obligatorio.", "RN-RE-18");
            }

            huella = Huella(comando);
            var previa = await solicitudes.ObtenerAsync(id, cancelacion);
            if (previa is not null)
            {
                return previa.TipoComando == accion && previa.HuellaContenido == huella
                    ? Resultado<T>.Exito(JsonSerializer.Deserialize<T>(previa.RespuestaSerializada, OpcionesJson)!)
                    : Resultado<T>.Fallo(CodigoError.SolicitudRepetidaConDatosDistintos,
                        "El RequestId ya se usó con datos distintos.", "RN-RE-18");
            }
        }

        var contexto = new ContextoOperacion(usuario, reloj.Ahora);
        Error error;
        try
        {
            var valor = await operacion(contexto);

            foreach (var entrada in contexto.EntradasAuditoria)
            {
                auditoria.Registrar(new EntradaAuditoria(usuario.UsuarioId, usuario.Rol, accion, entrada.TipoObjeto,
                    entrada.ObjetoId, entrada.DesastreId, contexto.Ahora, ResultadoOperacion.Exitoso, requestId, entrada.Detalle));
            }

            outbox.Agregar(contexto.Agregados.SelectMany(a => a.Eventos).ToList());

            if (requestId is { } solicitud)
            {
                solicitudes.Registrar(new SolicitudProcesada(solicitud, accion, huella!,
                    JsonSerializer.Serialize(valor, OpcionesJson)));
            }

            await unidadDeTrabajo.ConfirmarAsync(cancelacion);

            foreach (var agregado in contexto.Agregados)
            {
                agregado.LimpiarEventos();
            }

            return Resultado<T>.Exito(valor);
        }
        catch (ReglaDeNegocioException ex)
        {
            error = new Error(CodigoError.ReglaIncumplida, ex.Message, ex.Regla);
        }
        catch (DatosInvalidosException ex)
        {
            error = new Error(CodigoError.DatosInvalidos, ex.Message);
        }
        catch (ArgumentException ex)
        {
            error = new Error(CodigoError.DatosInvalidos, ex.Message);
        }
        catch (ElementoNoEncontradoException ex)
        {
            error = new Error(CodigoError.NoEncontrado, ex.Message);
        }
        catch (NoEncontradoException ex)
        {
            error = new Error(CodigoError.NoEncontrado, ex.Message);
        }
        catch (NoAutorizadoException ex)
        {
            error = new Error(CodigoError.NoAutorizado, ex.Message, "RN-RE-19");
        }
        catch (ConflictoDeVersionException ex)
        {
            error = new Error(CodigoError.ConflictoVersion, ex.Message, "RN-RE-18");
        }

        // Los rechazos se auditan en una escritura independiente para que no se pierdan
        // al revertir la operación (RN-RE-16).
        var objetivo = contexto.Objetivo;
        await auditoria.RegistrarRechazoAsync(new EntradaAuditoria(usuario.UsuarioId, usuario.Rol, accion, objetivo.Tipo,
            objetivo.Id, objetivo.DesastreId, contexto.Ahora, ResultadoOperacion.Rechazado, requestId,
            $"{error.Codigo}: {error.Mensaje}"), cancelacion);

        return Resultado<T>.Fallo(error);
    }

    private static string Huella(object comando)
    {
        var json = JsonSerializer.Serialize(comando, comando.GetType(), OpcionesJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
