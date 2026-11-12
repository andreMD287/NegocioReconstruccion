namespace Reconstruccion.Dominio.Comun;

/// <summary>Estados del proyecto (sección 5.1).</summary>
public enum EstadoProyecto
{
    Propuesto,
    Aprobado,
    Rechazado,
    EnEjecucion,
    Suspendido,
    Cancelado,
    Finalizado
}

/// <summary>Estados de la necesidad (sección 5.2).</summary>
public enum EstadoNecesidad
{
    Pendiente,
    Asignada,
    Atendida,
    Descartada
}

/// <summary>Estados del plan (sección 5.3).</summary>
public enum EstadoPlan
{
    Abierto,
    Completado,
    Cerrado
}

/// <summary>Estados del cambio presupuestal (sección 5.4).</summary>
public enum EstadoCambioPresupuestal
{
    Solicitado,
    Aprobado,
    Rechazado,
    Anulado
}

/// <summary>Tipo de proyecto sugerido por el nivel de daño (RN-RE-02).</summary>
public enum TipoProyecto
{
    Recuperacion,
    Reconstruccion
}

/// <summary>Tipos de alerta (RN-RE-21).</summary>
public enum TipoAlerta
{
    Sobrecosto,
    HitoVencido,
    OrigenModificado
}

public enum EstadoAlerta
{
    Activa,
    Resuelta
}

/// <summary>Tipos de documento del proyecto. El acta de entrega es requisito de Finalizar (RN-RE-12).</summary>
public enum TipoDocumento
{
    ActaEntrega,
    Contrato,
    Administrativo,
    Otro
}
