using Reconstruccion.Dominio.Comun;
using Reconstruccion.Dominio.Planes;
using Reconstruccion.Dominio.Proyectos;

namespace Reconstruccion.Dominio.Tests;

/// <summary>Construye agregados en estados conocidos para las pruebas.</summary>
internal static class Fabrica
{
    public static readonly DateTimeOffset Ahora = new(2026, 11, 12, 10, 0, 0, TimeSpan.FromHours(-5));

    public static DateOnly Hoy => DateOnly.FromDateTime(Ahora.Date);

    public static EvaluacionEntrante Evaluacion(Guid? evaluacionId = null, long version = 1, bool validada = true,
        bool requiereIntervencion = true) =>
        new(new EvaluacionOrigen(evaluacionId ?? Guid.NewGuid(), version, Guid.NewGuid(), "Escuela", "Grave",
                Ahora.AddDays(-3), new Ubicacion(4.6, -74.1, "Sede principal")),
            validada, requiereIntervencion);

    /// <summary>Plan con <paramref name="cantidad"/> necesidades recibidas en el ciclo 1.</summary>
    public static PlanReconstruccion PlanConNecesidades(int cantidad, out List<Guid> necesidadIds)
    {
        var plan = PlanReconstruccion.Abrir(Guid.NewGuid(), Guid.NewGuid());
        var evaluaciones = Enumerable.Range(0, cantidad).Select(_ => Evaluacion()).ToList();
        necesidadIds = plan.RecibirEvaluaciones(1, evaluaciones, Guid.NewGuid, Ahora)
            .Select(r => r.NecesidadId!.Value)
            .ToList();
        plan.LimpiarEventos();
        return plan;
    }

    public static ProyectoReconstruccion ProyectoPropuesto(out Guid formuladorId, Guid? necesidadId = null)
    {
        formuladorId = Guid.NewGuid();
        return ProyectoReconstruccion.Proponer(Guid.NewGuid(), Guid.NewGuid(), formuladorId, "Escuela La Esperanza",
            "Reconstrucción de aulas", TipoProyecto.Reconstruccion, [necesidadId ?? Guid.NewGuid()], Ahora);
    }

    /// <summary>Proyecto Aprobado con presupuesto de 1000, responsable, contratista y dos hitos (60/40).</summary>
    public static ProyectoReconstruccion ProyectoAprobado(out Guid formuladorId, Guid? necesidadId = null)
    {
        var proyecto = ProyectoPropuesto(out formuladorId, necesidadId);
        proyecto.EditarPresupuesto(1000m, [new Aporte("Nacion", 700m), new Aporte("Departamento", 300m)]);
        proyecto.AsignarParticipantes(new Responsable(Guid.NewGuid(), Guid.NewGuid()), new Contratista("900123", "Obras SAS"), Ahora);
        proyecto.DefinirCronograma(Hoy.AddDays(-30), Hoy.AddDays(60),
        [
            ("Cimentación", Hoy.AddDays(-30), Hoy.AddDays(10), 60m),
            ("Estructura", Hoy.AddDays(11), Hoy.AddDays(60), 40m)
        ], Guid.NewGuid);
        proyecto.Aprobar(Guid.NewGuid(), Ahora);
        proyecto.LimpiarEventos();
        return proyecto;
    }

    public static ProyectoReconstruccion ProyectoEnEjecucion(out Guid formuladorId, Guid? necesidadId = null)
    {
        var proyecto = ProyectoAprobado(out formuladorId, necesidadId);
        proyecto.Iniciar(Ahora);
        proyecto.LimpiarEventos();
        return proyecto;
    }

    /// <summary>Proyecto EnEjecucion que ya cumple todas las condiciones de Finalizar (RN-RE-12).</summary>
    public static ProyectoReconstruccion ProyectoListoParaFinalizar(Guid? necesidadId = null)
    {
        var proyecto = ProyectoEnEjecucion(out _, necesidadId);
        foreach (var hito in proyecto.Hitos)
        {
            proyecto.RegistrarAvance(hito.Id, 100m, Hoy, [], null, Guid.NewGuid(), Ahora);
        }

        proyecto.RegistrarCosto(900m, "Obra", Hoy, null, null, Guid.NewGuid(), Ahora);
        proyecto.AdjuntarDocumento(TipoDocumento.ActaEntrega, "Acta", "docs/acta.pdf", Guid.NewGuid(), Ahora);
        proyecto.LimpiarEventos();
        return proyecto;
    }

    public static ReglaDeNegocioException ViolaRegla(string regla, Action accion)
    {
        var excepcion = Assert.Throws<ReglaDeNegocioException>(accion);
        Assert.Equal(regla, excepcion.Regla);
        return excepcion;
    }
}
