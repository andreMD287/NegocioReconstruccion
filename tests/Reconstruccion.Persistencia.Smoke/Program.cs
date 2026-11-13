using Reconstruccion.Infraestructura.Persistencia.Repositorios;
using Reconstruccion.Dominio.Comun;
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

        var repositorioPlanesAlta = new RepositorioPlanesEf(escritura);
        var repositorioProyectosAlta = new RepositorioProyectosEf(escritura);

        repositorioPlanesAlta.Agregar(plan);
        repositorioProyectosAlta.Agregar(proyecto);

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

    // ==========================================
    // PRUEBA DE NECESIDADES Y UBICACION JSONB
    // ==========================================

    // ==========================================
    // PRUEBA DE CONCURRENCIA OPTIMISTA
    // ==========================================

    // ==========================================
    // PRUEBA REAL DE REPOSITORIOS
    // ==========================================

    Console.WriteLine("Iniciando prueba de repositorios EF Core.");

    using (var contextoRepositorios = new ReconstruccionDbContext(opciones))
    {
        await contextoRepositorios.Database
            .UseTransactionAsync(transaccion);

        var repositorioPlanes =
            new RepositorioPlanesEf(contextoRepositorios);

        var repositorioProyectos =
            new RepositorioProyectosEf(contextoRepositorios);

        // Recuperar el Plan por DesastreId
        var planRepositorio = await repositorioPlanes
            .ObtenerPorDesastreAsync(desastreId);

        if (planRepositorio is null)
            throw new Exception("El repositorio no recupero el Plan.");

        if (planRepositorio.Id != planId)
            throw new Exception("El repositorio recupero otro Plan.");

        // Recuperar el Proyecto por su identificador
        var proyectoRepositorio = await repositorioProyectos
            .ObtenerPorIdAsync(proyectoId);

        if (proyectoRepositorio is null)
            throw new Exception("El repositorio no recupero el Proyecto.");

        if (proyectoRepositorio.DesastreId != desastreId)
            throw new Exception("El Proyecto pertenece a otro desastre.");

        if (proyectoRepositorio.Presupuesto != 75000m)
            throw new Exception("El presupuesto no coincide.");

        if (!proyectoRepositorio.NecesidadIds.Contains(necesidadId))
            throw new Exception("No se recuperaron las necesidades.");

        if (proyectoRepositorio.Aportes.Count != 1)
            throw new Exception("No se recuperaron los aportes owned.");

        if (proyectoRepositorio.Responsable is null)
            throw new Exception("No se recupero el responsable.");

        // Ambos agregados deben permanecer tracked
        var estadoPlan = contextoRepositorios
            .Entry(planRepositorio).State;

        var estadoProyecto = contextoRepositorios
            .Entry(proyectoRepositorio).State;

        if (estadoPlan != EntityState.Unchanged)
            throw new Exception("El Plan no esta siendo rastreado.");

        if (estadoProyecto != EntityState.Unchanged)
            throw new Exception("El Proyecto no esta siendo rastreado.");

        Console.WriteLine(
            "Plan y Proyecto recuperados correctamente mediante repositorios."
        );

        Console.WriteLine(
            "Colecciones owned y seguimiento EF Core verificados."
        );

        // Comprobar que un proyecto inexistente devuelve null
        var inexistente = await repositorioProyectos
            .ObtenerPorIdAsync(Guid.NewGuid());

        if (inexistente is not null)
            throw new Exception("Se recupero un proyecto inexistente.");

        // Comprobar listado de proyectos en ejecucion
        var idsRepositorio = await repositorioProyectos
            .ListarIdsEnEjecucionAsync();

        var idsConsulta = await contextoRepositorios.Proyectos
            .Where(p => p.Estado == EstadoProyecto.EnEjecucion)
            .Select(p => p.Id)
            .ToListAsync();

        if (!idsRepositorio.OrderBy(id => id)
            .SequenceEqual(idsConsulta.OrderBy(id => id)))
        {
            throw new Exception(
                "La consulta de proyectos en ejecucion no coincide."
            );
        }

        Console.WriteLine("Consultas de repositorios verificadas.");
    }

    Console.WriteLine("PRUEBA DE REPOSITORIOS SUPERADA.");
    await PruebaAdaptadores.EjecutarAsync(opciones, transaccion);

    Console.WriteLine("Iniciando prueba de concurrencia.");

    using (var contextoA = new ReconstruccionDbContext(opciones))
    using (var contextoB = new ReconstruccionDbContext(opciones))
    {
        await contextoA.Database.UseTransactionAsync(transaccion);
        await contextoB.Database.UseTransactionAsync(transaccion);

        // Ambos leen el mismo proyecto ANTES de guardar.
        var proyectoA = await contextoA.Proyectos
            .SingleAsync(p => p.Id == proyectoId);

        var proyectoB = await contextoB.Proyectos
            .SingleAsync(p => p.Id == proyectoId);

        var versionOriginal = proyectoA.Version;

        if (proyectoB.Version != versionOriginal)
            throw new Exception("Los contextos no cargaron la misma version.");

        Console.WriteLine(
            $"Ambos contextos cargaron la version {versionOriginal}."
        );

        // El contexto A modifica el responsable.
        var nuevoUsuarioId = Guid.NewGuid();

        proyectoA.AsignarParticipantes(
            new Responsable(nuevoUsuarioId, Guid.NewGuid()),
            null,
            DateTimeOffset.UtcNow
        );

        await contextoA.SaveChangesAsync();

        Console.WriteLine(
            $"Contexto A guardo la version {proyectoA.Version}."
        );

        // El contexto B conserva la version anterior.
        proyectoB.AsignarParticipantes(
            null,
            new Contratista("PRUEBA-B", "Contratista B"),
            DateTimeOffset.UtcNow
        );

        var conflictoDetectado = false;

        try
        {
            await contextoB.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            conflictoDetectado = true;

            Console.WriteLine(
                "DbUpdateConcurrencyException detectada correctamente."
            );
        }

        if (!conflictoDetectado)
        {
            throw new Exception(
                "ERROR: El contexto B sobrescribio datos sin detectar conflicto."
            );
        }

        // Tercer contexto: comprobar el estado real almacenado.
        using (var contextoVerificacion =
            new ReconstruccionDbContext(opciones))
        {
            await contextoVerificacion.Database
                .UseTransactionAsync(transaccion);

            var proyectoFinal = await contextoVerificacion.Proyectos
                .SingleAsync(p => p.Id == proyectoId);

            if (proyectoFinal.Version != versionOriginal + 1)
                throw new Exception("La version final es incorrecta.");

            if (proyectoFinal.Responsable?.UsuarioId != nuevoUsuarioId)
                throw new Exception("No se conservo el cambio del contexto A.");

            if (proyectoFinal.Contratista?.Nombre != "Contratista de prueba")
                throw new Exception("El contexto B sobrescribio el contratista.");

            Console.WriteLine(
                "Version y participantes finales verificados."
            );

            Console.WriteLine(
                "El cambio obsoleto no sobrescribio los datos."
            );
        }
    }

    Console.WriteLine("PRUEBA DE CONCURRENCIA SUPERADA.");
    Console.WriteLine("Iniciando prueba de necesidad y JSONB.");

    var evaluacionId = Guid.NewGuid();

    var origenInicial = new EvaluacionOrigen(
        evaluacionId,
        1,
        Guid.NewGuid(),
        "Edificio",
        "Alto",
        DateTimeOffset.UtcNow,
        new Ubicacion(4.65, -74.10, "Ubicacion inicial")
    );

    var planConNecesidad = PlanReconstruccion.Abrir(
        Guid.NewGuid(),
        Guid.NewGuid()
    );

    planConNecesidad.RecibirEvaluaciones(
        1,
        new[] { new EvaluacionEntrante(origenInicial, true, true) },
        Guid.NewGuid,
        DateTimeOffset.UtcNow
    );

    if (planConNecesidad.Necesidades.Count != 1)
        throw new Exception("No se creo la necesidad.");

    var necesidadIdReal = planConNecesidad.Necesidades.Single().Id;
    var planConNecesidadId = planConNecesidad.Id;

    // A. Guardar necesidad en PostgreSQL
    using (var alta = new ReconstruccionDbContext(opciones))
    {
        await alta.Database.UseTransactionAsync(transaccion);

        alta.Planes.Add(planConNecesidad);

        await alta.SaveChangesAsync();
    }

    Console.WriteLine("Necesidad y ubicacion inicial guardadas.");

    // ==========================================
    // VERIFICAR INDICE UNICO EN POSTGRESQL
    // ==========================================

    Console.WriteLine("Probando rechazo de evaluacion duplicada.");

    await transaccion.SaveAsync("antes_duplicado");

    var duplicadoRechazado = false;

    try
    {
        await using var comando = new NpgsqlCommand(
            """
            INSERT INTO public.necesidades (
                "Id",
                evaluacion_id,
                version_evaluacion,
                activo_id,
                tipo_activo,
                nivel_dano,
                fecha_validacion,
                ubicacion,
                "CicloRecepcion",
                "Estado",
                "ProyectoId",
                "EvaluacionRevocada",
                "UltimaJustificacion",
                "PlanId"
            )
            SELECT
                @nuevoId,
                evaluacion_id,
                version_evaluacion,
                activo_id,
                tipo_activo,
                nivel_dano,
                fecha_validacion,
                ubicacion,
                "CicloRecepcion",
                "Estado",
                "ProyectoId",
                "EvaluacionRevocada",
                "UltimaJustificacion",
                "PlanId"
            FROM public.necesidades
            WHERE "Id" = @originalId;
            """,
            conexion,
            transaccion
        );

        comando.Parameters.AddWithValue("nuevoId", Guid.NewGuid());
        comando.Parameters.AddWithValue("originalId", necesidadIdReal);

        var filas = await comando.ExecuteNonQueryAsync();

        if (filas == 0)
            throw new Exception("No se encontro la necesidad original.");
    }
    catch (PostgresException ex)
        when (
            ex.SqlState == PostgresErrorCodes.UniqueViolation &&
            ex.ConstraintName ==
                "UX_necesidades_PlanId_evaluacion_id"
        )
    {
        duplicadoRechazado = true;

        Console.WriteLine(
            "PostgreSQL rechazo el duplicado con codigo 23505."
        );

        Console.WriteLine(
            $"Indice responsable: {ex.ConstraintName}"
        );
    }
    finally
    {
        await transaccion.RollbackAsync("antes_duplicado");
        await transaccion.ReleaseAsync("antes_duplicado");
    }

    if (!duplicadoRechazado)
        throw new Exception(
            "ERROR: PostgreSQL no rechazo la evaluacion duplicada."
        );

    Console.WriteLine("PRUEBA DE UNICIDAD SUPERADA.");

    // B. Recuperar, comprobar y corregir la evaluacion
    using (var actualizacion = new ReconstruccionDbContext(opciones))
    {
        await actualizacion.Database.UseTransactionAsync(transaccion);

        var planCargado = await actualizacion.Planes
            .Include(p => p.Necesidades)
            .Include(p => p.Alertas)
            .SingleAsync(p => p.Id == planConNecesidadId);

        var necesidadCargada = planCargado.Necesidades.Single();

        if (necesidadCargada.Id != necesidadIdReal)
            throw new Exception("La necesidad recuperada no coincide.");

        if (necesidadCargada.Origen.Ubicacion.Descripcion != "Ubicacion inicial")
            throw new Exception("No se recupero la ubicacion inicial.");

        if (necesidadCargada.Origen.VersionEvaluacion != 1)
            throw new Exception("No se recupero la version de evaluacion.");

        Console.WriteLine("Necesidad y JSONB recuperados correctamente.");

        var origenCorregido = origenInicial with
        {
            VersionEvaluacion = 2,
            Ubicacion = new Ubicacion(
                4.66,
                -74.09,
                "Ubicacion corregida"
            )
        };

        planCargado.AplicarCambioEvaluacion(
            evaluacionId,
            2,
            TipoCambioOrigen.Correccion,
            origenCorregido,
            DateTimeOffset.UtcNow
        );

        await actualizacion.SaveChangesAsync();

        Console.WriteLine("Correccion de evaluacion guardada.");
    }

    // C. Abrir otro contexto para comprobar los cambios
    using (var verificacion = new ReconstruccionDbContext(opciones))
    {
        await verificacion.Database.UseTransactionAsync(transaccion);

        var planFinal = await verificacion.Planes
            .Include(p => p.Necesidades)
            .Include(p => p.Alertas)
            .SingleAsync(p => p.Id == planConNecesidadId);

        var necesidadFinal = planFinal.Necesidades.Single();

        if (necesidadFinal.Origen.VersionEvaluacion != 2)
            throw new Exception("No se actualizo la version de evaluacion.");

        if (necesidadFinal.Origen.Ubicacion !=
            new Ubicacion(4.66, -74.09, "Ubicacion corregida"))
            throw new Exception("No se guardo la ubicacion corregida.");

        if (planFinal.Alertas.Count != 1)
            throw new Exception("No se recupero la alerta de correccion.");

        if (planFinal.Version <= 0)
            throw new Exception("No se conservo la version del Plan.");

        Console.WriteLine("Ubicacion corregida recuperada desde PostgreSQL.");
        Console.WriteLine("Alerta y version del Plan verificadas.");
        Console.WriteLine("PRUEBA JSONB SUPERADA.");
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