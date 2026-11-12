using Microsoft.EntityFrameworkCore;
using Npgsql;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;
using Reconstruccion.Infraestructura.Persistencia;

var connectionString =
    Environment.GetEnvironmentVariable("RECONSTRUCCION_DB_CONNECTION")
    ?? throw new InvalidOperationException("Falta la conexion PostgreSQL.");

await using var conexion = new NpgsqlConnection(connectionString);
await conexion.OpenAsync();

Console.WriteLine("Conexion PostgreSQL abierta correctamente.");

await using var transaccion = await conexion.BeginTransactionAsync();

var opciones = new DbContextOptionsBuilder<ReconstruccionDbContext>()
    .UseNpgsql(conexion)
    .Options;

var desastreId = Guid.NewGuid();
var planId = Guid.NewGuid();
var proyectoId = Guid.NewGuid();
var necesidadId = Guid.NewGuid();

var plan = PlanReconstruccion.Abrir(planId, desastreId);

var proyecto = ProyectoReconstruccion.Proponer(
    proyectoId,
    desastreId,
    Guid.NewGuid(),
    "Proyecto de prueba EF Core",
    "Prueba de guardado y recuperacion",
    default,
    new[] { necesidadId },
    DateTimeOffset.UtcNow
);

proyecto.EditarPresupuesto(
    75000m,
    new List<Aporte> { new("Fondo de prueba", 75000m) }
);

proyecto.AsignarParticipantes(
    new Responsable(Guid.NewGuid(), Guid.NewGuid()),
    new Contratista("PRUEBA-001", "Contratista de prueba"),
    DateTimeOffset.UtcNow
);

try
{
    // Primer contexto: INSERT
    using (var escritura = new ReconstruccionDbContext(opciones))
    {
        await escritura.Database.UseTransactionAsync(transaccion);

        escritura.Planes.Add(plan);
        escritura.Proyectos.Add(proyecto);

        var guardados = await escritura.SaveChangesAsync();

        Console.WriteLine($"SaveChanges correcto. Entradas escritas: {guardados}");
    }

    // Segundo contexto: SELECT y materializacion
    using (var lectura = new ReconstruccionDbContext(opciones))
    {
        await lectura.Database.UseTransactionAsync(transaccion);

        var planLeido = await lectura.Planes
            .SingleAsync(p => p.Id == planId);

        var proyectoLeido = await lectura.Proyectos
            .SingleAsync(p => p.Id == proyectoId);

        if (planLeido.DesastreId != desastreId)
            throw new Exception("El Plan recuperado no coincide.");

        if (proyectoLeido.Presupuesto != 75000m)
            throw new Exception("El presupuesto no se recupero correctamente.");

        if (!proyectoLeido.NecesidadIds.Contains(necesidadId))
            throw new Exception("No se recupero el identificador de necesidad.");

        if (proyectoLeido.Aportes.Count != 1 ||
            proyectoLeido.Aportes[0].Fuente != "Fondo de prueba")
            throw new Exception("No se recuperaron los aportes.");

        if (proyectoLeido.Responsable is null)
            throw new Exception("No se recupero el responsable.");

        if (proyectoLeido.Contratista?.Nombre != "Contratista de prueba")
            throw new Exception("No se recupero el contratista.");

        if (proyectoLeido.Version != proyecto.Version)
            throw new Exception("No se conservo la version del proyecto.");

        Console.WriteLine("Plan recuperado correctamente.");
        Console.WriteLine("Proyecto recuperado correctamente.");
        Console.WriteLine("Presupuesto, necesidades y aportes verificados.");
        Console.WriteLine("Responsable, contratista y version verificados.");
    }

    await transaccion.RollbackAsync();

    Console.WriteLine("ROLLBACK ejecutado: datos de prueba descartados.");
    Console.WriteLine("PRUEBA DE PERSISTENCIA SUPERADA.");
}
catch
{
    await transaccion.RollbackAsync();
    Console.WriteLine("ROLLBACK ejecutado por error.");
    throw;
}