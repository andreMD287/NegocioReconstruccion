using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Reconstruccion.Aplicacion.Puertos;

namespace Reconstruccion.Host.Seguridad;

/// <summary>Sección "Jwt" de la configuración.</summary>
public sealed class OpcionesJwt
{
    public string Emisor { get; set; } = "reconstruccion";

    public string Audiencia { get; set; } = "reconstruccion";

    /// <summary>Clave simétrica HMAC de al menos 32 caracteres. En producción va en un secreto, nunca en el repositorio.</summary>
    public string Clave { get; set; } = string.Empty;

    public SymmetricSecurityKey ClaveFirma() => new(Encoding.UTF8.GetBytes(Clave));
}

public static class AutenticacionJwt
{
    public const string RutaHub = "/hubs/reconstruccion";

    /// <summary>
    /// JWT para gRPC y SignalR. Los claims se validan al autenticar (sub, rol, organismo), de modo
    /// que <see cref="UsuarioActualAmbito"/> siempre encuentra datos válidos.
    /// </summary>
    public static IServiceCollection AgregarAutenticacionJwt(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var opciones = configuracion.GetSection("Jwt").Get<OpcionesJwt>() ?? new OpcionesJwt();
        if (opciones.Clave.Length < 32)
        {
            throw new InvalidOperationException("Jwt:Clave debe tener al menos 32 caracteres.");
        }

        servicios.AddSingleton(opciones);
        servicios.AddAuthorization();
        servicios.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = opciones.Emisor,
                    ValidAudience = opciones.Audiencia,
                    IssuerSigningKey = opciones.ClaveFirma(),
                    NameClaimType = Claims.Usuario,
                    RoleClaimType = Claims.Rol,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                o.Events = new JwtBearerEvents
                {
                    // SignalR envía el token por la cadena de consulta en WebSockets.
                    OnMessageReceived = ctx =>
                    {
                        var token = ctx.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments(RutaHub))
                        {
                            ctx.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        if (Claims.Validar(ctx.Principal!) is { } motivo)
                        {
                            ctx.Fail(motivo);
                        }

                        return Task.CompletedTask;
                    }
                };
            });

        return servicios;
    }

    /// <summary>Emite un token firmado. Solo lo expone el Host en Development, para la demostración.</summary>
    public static string EmitirToken(OpcionesJwt opciones, Guid usuarioId, Rol rol, Guid? organismoId, TimeSpan vigencia)
    {
        if (!Claims.RolesDeToken.Contains(rol))
        {
            throw new ArgumentException("El rol Sistema no se emite en tokens.", nameof(rol));
        }

        var claims = new List<Claim> { new(Claims.Usuario, usuarioId.ToString()), new(Claims.Rol, rol.ToString()) };
        if (organismoId is { } organismo)
        {
            claims.Add(new Claim(Claims.Organismo, organismo.ToString()));
        }

        var token = new JwtSecurityToken(opciones.Emisor, opciones.Audiencia, claims, expires: DateTime.UtcNow.Add(vigencia),
            signingCredentials: new SigningCredentials(opciones.ClaveFirma(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
