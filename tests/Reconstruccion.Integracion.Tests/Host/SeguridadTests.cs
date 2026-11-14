using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;
using Reconstruccion.Aplicacion.Puertos;
using Reconstruccion.Host.Seguridad;

namespace Reconstruccion.Integracion.Tests.Host;

/// <summary>Autenticación JWT e identidad del alcance (RN-RE-19).</summary>
public class SeguridadTests
{
    private static readonly OpcionesJwt Opciones = new() { Clave = "clave-de-prueba-con-mas-de-treinta-y-dos-caracteres" };

    [Fact]
    public void Un_token_emitido_se_valida_y_conserva_usuario_rol_y_organismo()
    {
        var usuario = Guid.NewGuid();
        var organismo = Guid.NewGuid();

        var principal = Validar(AutenticacionJwt.EmitirToken(Opciones, usuario, Rol.Autoridad, organismo, TimeSpan.FromMinutes(5)));

        Assert.Null(Claims.Validar(principal));
        Assert.Equal(usuario.ToString(), principal.FindFirstValue(Claims.Usuario));
        Assert.Equal("Autoridad", principal.FindFirstValue(Claims.Rol));
        Assert.Equal(organismo.ToString(), principal.FindFirstValue(Claims.Organismo));
    }

    [Fact]
    public void No_se_emiten_tokens_con_rol_Sistema() =>
        Assert.Throws<ArgumentException>(() => AutenticacionJwt.EmitirToken(Opciones, Guid.NewGuid(), Rol.Sistema, null, TimeSpan.FromMinutes(5)));

    [Theory]
    [InlineData("no-guid", "Autoridad")]
    [InlineData("8b1e2c4a-1111-4a3b-9c1d-2f3e4a5b6c7d", "Sistema")]
    [InlineData("8b1e2c4a-1111-4a3b-9c1d-2f3e4a5b6c7d", "Administrador")]
    [InlineData("8b1e2c4a-1111-4a3b-9c1d-2f3e4a5b6c7d", "autoridad")]
    public void Se_rechazan_tokens_con_claims_invalidos(string usuario, string rol)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.Usuario, usuario), new Claim(Claims.Rol, rol)], "jwt"));

        Assert.NotNull(Claims.Validar(principal));
    }

    [Fact]
    public void El_usuario_actual_sale_de_los_claims_de_la_peticion()
    {
        var usuario = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(Claims.Usuario, usuario.ToString()), new Claim(Claims.Rol, "Responsable")], "jwt"))
        };

        var actual = new UsuarioActualAmbito(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(usuario, actual.UsuarioId);
        Assert.Equal(Rol.Responsable, actual.Rol);
        Assert.Null(actual.OrganismoId);
    }

    [Fact]
    public void Los_procesos_internos_actuan_como_Sistema_sin_peticion()
    {
        var actual = new UsuarioActualAmbito(new HttpContextAccessor());

        actual.ComoSistema();

        Assert.Equal(Rol.Sistema, actual.Rol);
        Assert.Equal(UsuarioActualAmbito.IdSistema, actual.UsuarioId);
    }

    [Fact]
    public void Sin_usuario_autenticado_no_hay_identidad()
    {
        var actual = new UsuarioActualAmbito(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Throws<InvalidOperationException>(() => actual.UsuarioId);
    }

    private static ClaimsPrincipal Validar(string token) =>
        new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
        {
            ValidIssuer = Opciones.Emisor,
            ValidAudience = Opciones.Audiencia,
            IssuerSigningKey = Opciones.ClaveFirma()
        }, out _);
}
