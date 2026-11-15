using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Reconstruccion.Contratos.V1;

var builder = WebApplication.CreateBuilder(args);

var puertoGrpc = Puerto("CIERRE_SIMULADO_GRPC_PUERTO", 56051);
var puertoAdmin = Puerto("CIERRE_SIMULADO_ADMIN_PUERTO", 56052);

if (puertoGrpc == puertoAdmin)
{
    throw new InvalidOperationException(
        "Los puertos gRPC y administracion deben ser diferentes.");
}

builder.WebHost.ConfigureKestrel(opciones =>
{
    opciones.ListenAnyIP(
        puertoGrpc,
        endpoint => endpoint.Protocols = HttpProtocols.Http2);

    if (Environment.GetEnvironmentVariable(
            "CIERRE_SIMULADO_ADMIN_CONTENEDOR") == "1")
    {
        opciones.ListenAnyIP(
            puertoAdmin,
            endpoint => endpoint.Protocols = HttpProtocols.Http1);
    }
    else
    {
        opciones.ListenLocalhost(
            puertoAdmin,
            endpoint => endpoint.Protocols = HttpProtocols.Http1);
    }
});

builder.Services.AddGrpc();
builder.Services.AddSingleton<EstadoCierre>();
builder.Services.AddSingleton<RegistroCierre>();

var app = builder.Build();

app.Use(async (contexto, siguiente) =>
{
    if (contexto.Request.Path.StartsWithSegments("/admin") &&
        contexto.Connection.LocalPort != puertoAdmin)
    {
        contexto.Response.StatusCode = 404;
        return;
    }

    await siguiente(contexto);
});

app.MapGrpcService<CierreSimuladoServicio>();

app.MapGet("/salud", () => Results.Ok(new
{
    servicio = "CierreSimulado",
    estado = "activo"
}));

app.MapGet(
    "/admin/estado",
    (EstadoCierre estado, RegistroCierre registro) =>
        Results.Ok(new
        {
            modo = estado.Nombre,
            notificacionesAceptadas = registro.Cantidad
        }));

app.MapGet(
    "/admin/notificaciones",
    (RegistroCierre registro) =>
        Results.Ok(registro.Listar()));

app.MapPost(
    "/admin/modo/{modo}",
    (string modo, EstadoCierre estado) =>
    {
        ModoCierre? seleccionado = modo.ToLowerInvariant() switch
        {
            "aceptar" => ModoCierre.Aceptar,
            "rechazar" => ModoCierre.Rechazar,
            "no-disponible" => ModoCierre.NoDisponible,
            _ => null
        };

        if (seleccionado is null)
        {
            return Results.BadRequest(new
            {
                error = "Modos: aceptar, rechazar, no-disponible"
            });
        }

        estado.Cambiar(seleccionado.Value);

        return Results.Ok(new
        {
            modo = estado.Nombre
        });
    });

app.Logger.LogInformation(
    "CierreSimulado iniciado. gRPC={Grpc}, Admin={Admin}",
    puertoGrpc,
    puertoAdmin);

app.Run();

static int Puerto(string variable, int defecto)
{
    var texto = Environment.GetEnvironmentVariable(variable);

    if (string.IsNullOrWhiteSpace(texto))
        return defecto;

    if (!int.TryParse(texto, out var puerto) ||
        puerto < 1 || puerto > 65535)
    {
        throw new InvalidOperationException(
            $"Puerto invalido: {variable}");
    }

    return puerto;
}

internal enum ModoCierre
{
    Aceptar,
    Rechazar,
    NoDisponible
}

internal sealed class EstadoCierre
{
    private int _modo = (int)ModoCierre.Aceptar;

    public ModoCierre Modo =>
        (ModoCierre)Volatile.Read(ref _modo);

    public string Nombre => Modo switch
    {
        ModoCierre.Aceptar => "aceptar",
        ModoCierre.Rechazar => "rechazar",
        ModoCierre.NoDisponible => "no-disponible",
        _ => "desconocido"
    };

    public void Cambiar(ModoCierre modo)
    {
        Volatile.Write(ref _modo, (int)modo);
    }
}

internal sealed record NotificacionCierre(
    string Operacion,
    Guid DesastreId,
    long VersionPlan,
    DateTimeOffset RegistradaEnUtc);

internal sealed class RegistroCierre
{
    private readonly object _bloqueo = new();

    private readonly HashSet<string> _claves = [];
    private readonly List<NotificacionCierre> _registros = [];

    private readonly string _archivo;

    public RegistroCierre(IConfiguration configuracion)
    {
        var ruta = configuracion["CIERRE_SIMULADO_ARCHIVO"];

        if (string.IsNullOrWhiteSpace(ruta))
        {
            ruta = Path.Combine(
                AppContext.BaseDirectory,
                "datos",
                "notificaciones.jsonl");
        }

        _archivo = Path.GetFullPath(ruta);

        Directory.CreateDirectory(
            Path.GetDirectoryName(_archivo)!);

        if (!File.Exists(_archivo))
            return;

        foreach (var linea in File.ReadLines(_archivo))
        {
            if (string.IsNullOrWhiteSpace(linea))
                continue;

            var registro =
                JsonSerializer.Deserialize<NotificacionCierre>(linea)
                ?? throw new InvalidDataException(
                    "Notificacion persistida invalida.");

            var clave = Clave(
                registro.Operacion,
                registro.DesastreId,
                registro.VersionPlan);

            if (_claves.Add(clave))
                _registros.Add(registro);
        }
    }

    private static string Clave(
        string operacion,
        Guid desastreId,
        long version) =>
        $"{operacion}:{desastreId:D}:{version}";

    public int Cantidad
    {
        get
        {
            lock (_bloqueo)
                return _registros.Count;
        }
    }

    public bool Existe(
        string operacion,
        Guid desastreId,
        long version)
    {
        lock (_bloqueo)
        {
            return _claves.Contains(
                Clave(operacion, desastreId, version));
        }
    }

    public bool RegistrarSiNuevo(
        string operacion,
        Guid desastreId,
        long version)
    {
        lock (_bloqueo)
        {
            var clave = Clave(operacion, desastreId, version);

            if (_claves.Contains(clave))
                return false;

            var registro = new NotificacionCierre(
                operacion,
                desastreId,
                version,
                DateTimeOffset.UtcNow);

            var linea = JsonSerializer.Serialize(registro) + "\n";
            var bytes = Encoding.UTF8.GetBytes(linea);

            using (var archivo = new FileStream(
                _archivo,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read))
            {
                archivo.Write(bytes);
                archivo.Flush(flushToDisk: true);
            }

            _claves.Add(clave);
            _registros.Add(registro);

            return true;
        }
    }

    public NotificacionCierre[] Listar()
    {
        lock (_bloqueo)
        {
            return _registros.ToArray();
        }
    }
}

internal sealed class CierreSimuladoServicio(
    EstadoCierre estado,
    RegistroCierre registro,
    ILogger<CierreSimuladoServicio> logger)
    : CierreService.CierreServiceBase
{
    public override Task<RespuestaCierreGrpc> NotificarPlanCompletado(
        NotificarPlanCompletadoRequest solicitud,
        ServerCallContext contexto)
    {
        return Task.FromResult(Procesar(
            "PlanCompletado",
            solicitud.DesastreId,
            solicitud.VersionPlan,
            contexto));
    }

    public override Task<RespuestaCierreGrpc> NotificarReapertura(
        NotificarReaperturaRequest solicitud,
        ServerCallContext contexto)
    {
        return Task.FromResult(Procesar(
            "Reapertura",
            solicitud.DesastreId,
            solicitud.VersionPlan,
            contexto));
    }

    private RespuestaCierreGrpc Procesar(
        string operacion,
        string desastreTexto,
        long version,
        ServerCallContext contexto)
    {
        contexto.CancellationToken.ThrowIfCancellationRequested();

        if (!Guid.TryParse(desastreTexto, out var desastreId) ||
            desastreId == Guid.Empty)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "desastre_id debe ser un GUID valido."));
        }

        if (version < 0)
        {
            throw new RpcException(new Status(
                StatusCode.InvalidArgument,
                "version_plan no puede ser negativa."));
        }

        if (estado.Modo == ModoCierre.NoDisponible)
        {
            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "Cierre simulado no disponible."));
        }

        if (registro.Existe(operacion, desastreId, version))
        {
            logger.LogInformation(
                "Duplicado confirmado: {Operacion} {Desastre} v{Version}",
                operacion, desastreId, version);

            return new RespuestaCierreGrpc { Aceptada = true };
        }

        if (estado.Modo == ModoCierre.Rechazar)
        {
            logger.LogWarning(
                "Rechazo simulado: {Operacion} {Desastre} v{Version}",
                operacion, desastreId, version);

            return new RespuestaCierreGrpc
            {
                Aceptada = false,
                Motivo = "Rechazo configurado para la demostracion."
            };
        }

        bool nueva;

        try
        {
            nueva = registro.RegistrarSiNuevo(
                operacion, desastreId, version);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "No se pudo persistir la notificacion.");

            throw new RpcException(new Status(
                StatusCode.Unavailable,
                "No se pudo registrar la notificacion."));
        }

        logger.LogInformation(
            "Aceptada: Nueva={Nueva}, {Operacion}, {Desastre}, v{Version}",
            nueva, operacion, desastreId, version);

        return new RespuestaCierreGrpc
        {
            Aceptada = true
        };
    }
}