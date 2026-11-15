using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reconstruccion.Contratos.V1;
using Reconstruccion.Infraestructura.Persistencia;
using Testcontainers.PostgreSql;

namespace Reconstruccion.Integracion.Tests.PuntaAPunta;

/// <summary>
/// Recorre el Escenario E completo (sección 5) a través del Host real levantado en memoria
/// (<see cref="WebApplicationFactory{TEntryPoint}"/>), con PostgreSQL en un contenedor: evaluaciones
/// de Caracterización, creación y ciclo de vida del proyecto, y el plan llegando a Completado.
/// No hay un Cierre configurado, así que el plan se queda en Completado (sección 9.5, tercera
/// prueba de fallo: Cierre no disponible); eso también se verifica.
/// </summary>
public sealed class FlujoReconstruccionEnContenedorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _contenedor = new PostgreSqlBuilder("postgres:16.13").Build();

    private WebApplicationFactory<Program>? _fabrica;
    private HttpClient _http = null!;
    private GrpcChannel _canal = null!;

    public async Task InitializeAsync()
    {
        await _contenedor.StartAsync();

        // UseSetting (y no ConfigureAppConfiguration) porque Composicion.cs lee la cadena de
        // conexión de forma inmediata, mientras se arma el WebApplicationBuilder dentro de
        // Program.cs; ConfigureAppConfiguration se aplica después de que ese código ya corrió,
        // así que un appsettings.Development.json con valores propios lo ganaría igual.
        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Reconstruccion", _contenedor.GetConnectionString());
            builder.UseSetting("Jwt:Clave", "clave-de-prueba-punta-a-punta-con-mas-de-treinta-y-dos-caracteres");
            builder.UseSetting("Vigilancia:Habilitada", "false");
            builder.UseSetting("Publicador:IntervaloSegundos", "1");
        });

        using (var alcance = _fabrica.Services.CreateScope())
        {
            await alcance.ServiceProvider.GetRequiredService<ReconstruccionDbContext>().Database.MigrateAsync();
        }

        _http = _fabrica.CreateClient();
        _canal = GrpcChannel.ForAddress(_http.BaseAddress!, new GrpcChannelOptions { HttpClient = _http });
    }

    public async Task DisposeAsync()
    {
        _canal.Dispose();
        _http.Dispose();
        if (_fabrica is not null)
        {
            await _fabrica.DisposeAsync();
        }

        await _contenedor.DisposeAsync();
    }

    [Fact]
    public async Task El_escenario_E_llega_de_las_evaluaciones_al_plan_Completado()
    {
        var caracterizacion = await TokenAsync("Caracterizacion");
        var formulador = await TokenAsync("Responsable");
        var autoridad = await TokenAsync("Autoridad");

        var proyectos = new ProyectosService.ProyectosServiceClient(_canal);
        var caracterizacionServicio = new CaracterizacionService.CaracterizacionServiceClient(_canal);
        var consultas = new ConsultasService.ConsultasServiceClient(_canal);
        var desastreId = Guid.NewGuid().ToString();

        // 1-2: Caracterización envía dos evaluaciones; el Responsable crea el proyecto.
        var recepcion = await caracterizacionServicio.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId, VersionCiclo = 1, Evaluaciones = { Evaluacion(), Evaluacion() } },
            caracterizacion, cancellationToken: CancellationToken.None);
        Assert.Equal(2, recepcion.NecesidadesCreadas.Count);

        var creado = await proyectos.CrearProyectoAsync(new CrearProyectoRequest
        {
            RequestId = Guid.NewGuid().ToString(), DesastreId = desastreId, ExpectedVersionPlan = recepcion.VersionPlan,
            Nombre = "Escuela y puente", Descripcion = "Prueba de punta a punta", Tipo = TipoProyectoGrpc.TipoProyectoReconstruccion,
            NecesidadIds = { recepcion.NecesidadesCreadas }
        }, formulador, cancellationToken: CancellationToken.None);

        // Solo el formulador opera el proyecto en Propuesto (RN-RE-19).
        var intentoAjeno = await InvocarAsync(() => proyectos.EditarPresupuestoPropuestoAsync(new EditarPresupuestoPropuestoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = creado.NuevaVersion,
            PresupuestoDecimal = "1.00", Aportes = { new AporteDtoGrpc { Fuente = "x", MontoDecimal = "1.00" } }
        }, autoridad, cancellationToken: CancellationToken.None));
        Assert.Equal(StatusCode.PermissionDenied, intentoAjeno.StatusCode);

        // 3-10: presupuesto, participantes, cronograma, aprobación, inicio, avances, costo y acta.
        var version = (await proyectos.EditarPresupuestoPropuestoAsync(new EditarPresupuestoPropuestoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = creado.NuevaVersion,
            PresupuestoDecimal = "1000.00", Aportes = { new AporteDtoGrpc { Fuente = "Nación", MontoDecimal = "1000.00" } }
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        var responsableId = Guid.NewGuid().ToString();
        version = (await proyectos.AsignarParticipantesAsync(new AsignarParticipantesRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
            Responsable = new ResponsableDtoGrpc { UsuarioId = ExtraerUsuarioId(formulador), OrganismoId = Guid.NewGuid().ToString() },
            Contratista = new ContratistaDtoGrpc { Identificacion = "900", Nombre = "Obras SAS" }
        }, autoridad, cancellationToken: CancellationToken.None)).NuevaVersion;

        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        version = (await proyectos.DefinirCronogramaAsync(new DefinirCronogramaRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
            FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(10)),
            Hitos = { new HitoDtoGrpc { Nombre = "Única etapa", FechaInicio = Fecha(hoy), FechaFin = Fecha(hoy.AddDays(10)), PesoDecimal = "100.00" } }
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        version = (await proyectos.AprobarProyectoAsync(new AprobarProyectoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version
        }, autoridad, cancellationToken: CancellationToken.None)).NuevaVersion;

        version = (await proyectos.IniciarProyectoAsync(new IniciarProyectoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        var detalle = await consultas.ConsultarDetalleProyectoAsync(
            new ConsultarDetalleProyectoRequest { ProyectoId = creado.ProyectoId }, autoridad, cancellationToken: CancellationToken.None);
        var hito = Assert.Single(detalle.Hitos);

        version = (await proyectos.RegistrarAvanceAsync(new RegistrarAvanceRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version, HitoId = hito.HitoId,
            PorcentajeDecimal = "100.00", FechaEfectiva = Fecha(hoy)
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        version = (await proyectos.RegistrarCostoAsync(new RegistrarCostoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
            MontoDecimal = "900.00", Concepto = "Obra", FechaEfectiva = Fecha(hoy)
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        version = (await proyectos.AdjuntarDocumentoAsync(new AdjuntarDocumentoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version,
            Tipo = TipoDocumentoGrpc.TipoDocumentoActaEntrega, Nombre = "Acta", Ubicacion = "acta.pdf"
        }, formulador, cancellationToken: CancellationToken.None)).NuevaVersion;

        // Señal de fin de caracterización: lleva el plan a punto de completarse (RN-RE-22).
        var fin = await caracterizacionServicio.RecibirFinCaracterizacionAsync(
            new RecibirFinCaracterizacionRequest { DesastreId = desastreId, VersionCiclo = 1, CantidadEsperada = 2 },
            caracterizacion, cancellationToken: CancellationToken.None);
        Assert.Equal(RespuestaFinCaracterizacionGrpc.RespuestaFinAceptada, fin.Respuesta);

        // Finalizar atiende las necesidades y, al no quedar ninguna pendiente, completa el plan.
        var finalizado = await proyectos.FinalizarProyectoAsync(new FinalizarProyectoRequest
        {
            RequestId = Guid.NewGuid().ToString(), ProyectoId = creado.ProyectoId, ExpectedVersion = version, ExpectedVersionPlan = fin.VersionPlan
        }, formulador, cancellationToken: CancellationToken.None);
        Assert.True(finalizado.NuevaVersionPlan >= fin.VersionPlan);

        // El publicador del outbox entrega los eventos; sin Cierre configurado, el plan queda
        // Completado (no Cerrado) y el seguimiento consolidado lo refleja (sección 9.5).
        SeguimientoConsolidadoResponse seguimiento = null!;
        for (var intento = 0; intento < 20; intento++)
        {
            seguimiento = await consultas.ConsultarSeguimientoConsolidadoAsync(
                new ConsultarSeguimientoConsolidadoRequest { DesastreId = desastreId }, autoridad, cancellationToken: CancellationToken.None);
            if (seguimiento.EstadoPlan == EstadoPlanGrpc.EstadoPlanCompletado)
            {
                break;
            }

            await Task.Delay(500, CancellationToken.None);
        }

        Assert.Equal(EstadoPlanGrpc.EstadoPlanCompletado, seguimiento.EstadoPlan);
        Assert.Equal("900.00", seguimiento.CostoEjecutadoDecimal);
        Assert.Equal(1, seguimiento.ProyectosPorEstado.Single(p => p.Estado == EstadoProyectoGrpc.EstadoProyectoFinalizado).Cantidad);
    }

    [Fact]
    public async Task Una_evaluacion_repetida_no_duplica_la_necesidad()
    {
        var caracterizacion = await TokenAsync("Caracterizacion");
        var servicio = new CaracterizacionService.CaracterizacionServiceClient(_canal);
        var desastreId = Guid.NewGuid().ToString();
        var evaluacion = Evaluacion();

        var primera = await servicio.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId, VersionCiclo = 1, Evaluaciones = { evaluacion } },
            caracterizacion, cancellationToken: CancellationToken.None);
        var segunda = await servicio.RecibirNecesidadesAsync(
            new RecibirNecesidadesRequest { DesastreId = desastreId, VersionCiclo = 1, Evaluaciones = { evaluacion } },
            caracterizacion, cancellationToken: CancellationToken.None);

        Assert.Single(primera.NecesidadesCreadas);
        Assert.Empty(segunda.NecesidadesCreadas);
        Assert.Equal(MotivoEvaluacionNoAceptadaGrpc.MotivoEvaluacionRepetida, Assert.Single(segunda.NoAceptadas).Motivo);
    }

    // --- Ayudas -----------------------------------------------------------------------------

    private async Task<Metadata> TokenAsync(string rol)
    {
        var respuesta = await _http.PostAsJsonAsync("/dev/token", new { rol, usuarioId = Guid.NewGuid() }, CancellationToken.None);
        respuesta.EnsureSuccessStatusCode();
        var cuerpo = await respuesta.Content.ReadFromJsonAsync<RespuestaToken>(CancellationToken.None);
        return new Metadata { { "Authorization", $"Bearer {cuerpo!.Token}" } };
    }

    private static string ExtraerUsuarioId(Metadata encabezados)
    {
        var token = encabezados.Single(e => e.Key == "authorization").Value["Bearer ".Length..];
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(token);
        return jwt.Claims.Single(c => c.Type == "sub").Value;
    }

    private static async Task<RpcException> InvocarAsync<T>(Func<AsyncUnaryCall<T>> llamada)
    {
        try
        {
            await llamada().ResponseAsync;
            throw new InvalidOperationException("Se esperaba un RpcException y la llamada tuvo éxito.");
        }
        catch (RpcException ex)
        {
            return ex;
        }
    }

    private static EvaluacionRecibida Evaluacion() => new()
    {
        EvaluacionId = Guid.NewGuid().ToString(), VersionEvaluacion = 1, ActivoId = Guid.NewGuid().ToString(),
        TipoActivo = "Escuela", NivelDano = "Grave", FechaValidacion = Timestamp.FromDateTime(DateTime.UtcNow),
        Ubicacion = new UbicacionEvaluacion { Latitud = 4.6, Longitud = -74.1 }, Validada = true, RequiereIntervencion = true
    };

    private static string Fecha(DateOnly fecha) => fecha.ToString("yyyy-MM-dd");

    private sealed record RespuestaToken(string Token);
}
