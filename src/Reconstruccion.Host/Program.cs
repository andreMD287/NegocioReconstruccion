using Reconstruccion.Adaptadores.Grpc.Servicios;
using Reconstruccion.Adaptadores.TiempoReal;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Host;
using Reconstruccion.Host.Seguridad;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AgregarAutenticacionJwt(builder.Configuration);
builder.Services.AgregarReconstruccion(builder.Configuration);

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

// Contratos gRPC de la sección 8.1. La autorización por rol la aplica Aplicación (RN-RE-19).
app.MapGrpcService<ProyectosServicioGrpc>().RequireAuthorization();
app.MapGrpcService<CaracterizacionServicioGrpc>().RequireAuthorization();
app.MapGrpcService<ConsultasServicioGrpc>().RequireAuthorization();

// Notificaciones en tiempo real (sección 8.2).
app.MapHub<ReconstruccionHub>(AutenticacionJwt.RutaHub).RequireAuthorization();

app.MapGet("/", () => "Reconstruccion: servicios gRPC y hub SignalR en " + AutenticacionJwt.RutaHub);

if (app.Environment.IsDevelopment())
{
    // Solo para la demostración: emite tokens de prueba. Nunca se expone fuera de Development.
    app.MapPost("/dev/token", (SolicitudToken solicitud, OpcionesJwt opciones) =>
    {
        if (!Enum.TryParse<Rol>(solicitud.Rol, out var rol) || !Claims.RolesDeToken.Contains(rol))
        {
            return Results.BadRequest("Rol no válido. Use Responsable, Autoridad, Auditor o Caracterizacion.");
        }

        var token = AutenticacionJwt.EmitirToken(opciones, solicitud.UsuarioId ?? Guid.NewGuid(), rol, solicitud.OrganismoId,
            TimeSpan.FromHours(8));
        return Results.Ok(new { token });
    });
}

app.Run();

/// <summary>Solicitud del endpoint de tokens de demostración.</summary>
public sealed record SolicitudToken(string Rol, Guid? UsuarioId, Guid? OrganismoId);

/// <summary>Expuesto para las pruebas de integración con WebApplicationFactory.</summary>
public partial class Program;
