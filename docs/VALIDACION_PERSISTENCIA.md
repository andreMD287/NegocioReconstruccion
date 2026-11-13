# Validación técnica de Persistencia

## 1. Información general

**Proyecto:** NegocioReconstruccion

**Rama:** feature/infraestructura

**Commit de referencia:** cc39f495add80e3619356b3aee6e36094af941e1

**Tecnologías:** .NET 10, C#, Entity Framework Core, PostgreSQL, xUnit, Docker y Testcontainers.

**Objetivo:** documentar la validación de los principales comportamientos de Persistencia y su incorporación a pruebas automatizadas.

## 2. Resultados de validación

| Proyecto | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|
| Reconstruccion.Dominio.Tests | 62 | 0 | 0 |
| Reconstruccion.Aplicacion.Tests | 48 | 0 | 0 |
| Reconstruccion.Integracion.Tests | 14 | 0 | 0 |
| **Total** | **124** | **0** | **0** |

La última ejecución completa finalizó satisfactoriamente.

La solución también compiló con TreatWarningsAsErrors=true.

Las pruebas de persistencia que requieren PostgreSQL utilizaron bases de datos aisladas mediante Testcontainers.

El proyecto de integración también contiene pruebas que no necesitan una base de datos, como las verificaciones de configuración y dependencias.

## 3. Matriz de trazabilidad del Smoke

| Smoke original | Comportamiento comprobado | Prueba xUnit correspondiente |
|---|---|---|
| Program.cs | Guardado y recuperación de Plan y Proyecto | RepositoriosPostgreSqlEnContenedorTests |
| Program.cs | Propiedades, colecciones y seguimiento EF Core | RepositoriosPostgreSqlEnContenedorTests |
| Program.cs | Concurrencia optimista de participantes | ConcurrenciaParticipantesPostgreSqlEnContenedorTests |
| Program.cs | Persistencia y corrección de ubicación JSONB | NecesidadesJsonbPostgreSqlEnContenedorTests |
| Program.cs | Rechazo de evaluación duplicada | NecesidadesJsonbPostgreSqlEnContenedorTests |
| PruebaRegistroDependencias.cs | Registro de seis contratos y alcances de DbContext | RegistroDependenciasPersistenciaTests |
| PruebaAdaptadores.cs | Auditoría de rechazo independiente | AtomicidadAuditoriaPostgreSqlEnContenedorTests |
| PruebaUnidadDeTrabajo.cs | Atomicidad y ausencia de registros parciales | AtomicidadAuditoriaPostgreSqlEnContenedorTests |
| PruebaUnidadDeTrabajo.cs | Conflictos por Plan y RequestId duplicados | ConflictosPostgreSqlEnContenedorTests |
| PruebaOutbox.cs | Eventos, serialización, metadatos y secuencias | OutboxPostgreSqlEnContenedorTests |
| PruebaConcurrenciaCostos.cs | Concurrencia de costos y consistencia | ConcurrenciaCostosPostgreSqlEnContenedorTests |

Esta matriz relaciona comportamientos equivalentes o comparables. No certifica que todas las aserciones del Smoke se hayan reproducido literalmente.

## 4. Pruebas complementarias

### Migraciones

MigracionesPostgreSqlEnContenedorTests verifica la aplicación de las migraciones y los índices esperados en PostgreSQL.

### Modelo del Plan

PlanModeloTests verifica la configuración de las entidades y componentes del agregado Plan.

### Modelo del Proyecto

ProyectoModeloTests verifica la configuración de entidades y colecciones del agregado Proyecto.

## 5. Garantías técnicas verificadas

### 5.1 Atomicidad

Se comprobó que una operación fallida por duplicidad no persiste parcialmente el Plan, su auditoría, su solicitud ni su evento Outbox.

### 5.2 Concurrencia optimista

Dos contextos que cargan la misma versión no pueden confirmar modificaciones incompatibles sin detectar el conflicto.

La verificación incluye participantes de Proyecto y el escenario específico de costos.

### 5.3 Auditoría independiente

Se validó que un registro de rechazo confirmado mediante una conexión independiente puede conservarse después del rollback de la operación principal.

### 5.4 Conflictos e idempotencia

Se probaron conflictos asociados a Planes duplicados y RequestId duplicados, incluyendo su traducción a las excepciones de aplicación correspondientes.

### 5.5 JSONB

Se comprobó que las ubicaciones se almacenan como objetos JSONB y que sus correcciones se recuperan correctamente desde un nuevo DbContext.

También se validaron los cambios de versión de evaluación y las alertas relacionadas.

### 5.6 Unicidad de evaluaciones

PostgreSQL rechazó una evaluación duplicada dentro de un Plan mediante el índice UX_necesidades_PlanId_evaluacion_id y el código de error 23505.

### 5.7 Outbox

Se validó el catálogo de 17 tipos de eventos incluidos en la prueba, su serialización, los metadatos y su ordenamiento por secuencia.

### 5.8 Repositorios EF Core

Se comprobaron consultas de Planes y Proyectos, seguimiento de entidades, materialización de colecciones y filtrado de proyectos en ejecución.

### 5.9 Inyección de dependencias

Se verificó la resolución de los seis contratos de Persistencia, el DbContext compartido dentro de un alcance y los contextos independientes entre alcances.

## 6. Diferencias respecto al Smoke original

El Smoke original utiliza transacciones externas, savepoints y rollback para ejecutar varias verificaciones en una base de laboratorio.

Las pruebas xUnit utilizan principalmente PostgreSQL aislado mediante Testcontainers.

Ambas estrategias permiten comprobar comportamientos importantes, pero no son idénticas en su preparación y limpieza.

No se ha calculado cobertura porcentual de líneas, ramas o mutaciones.

Tampoco se ha certificado equivalencia literal de todas las aserciones originales.

La evidencia disponible corresponde a ejecuciones locales; no demuestra por sí misma ejecución automatizada en CI/CD.

No se afirma que feature/infraestructura haya sido fusionada a main.

## 7. Reproducción de las pruebas

Desde la raíz del repositorio ejecutar:

**Compilación estricta:**

dotnet build NegocioReconstruccion.slnx -p:TreatWarningsAsErrors=true

**Pruebas de toda la solución:**

dotnet test NegocioReconstruccion.slnx --logger "console;verbosity=minimal"

Para ejecutar las pruebas que utilizan PostgreSQL, Docker Desktop debe estar disponible.

## 8. Estado del repositorio

**Repositorio:** https://github.com/andreMD287/NegocioReconstruccion

**Rama de trabajo:** feature/infraestructura

**Commit validado:** cc39f495add80e3619356b3aee6e36094af941e1

La rama estaba sincronizada y sin modificaciones locales pendientes al iniciar la preparación del informe.

El Smoke original se conserva y no ha sido reemplazado por las pruebas nuevas.

## 9. Conclusión técnica

Se incorporaron pruebas automatizadas que cubren los principales escenarios de Persistencia previamente revisados mediante Smoke.

La ejecución verificada finalizó con 124 pruebas aprobadas, ninguna fallida y ninguna omitida.

Las garantías comprobadas incluyen atomicidad, concurrencia optimista, auditoría independiente, conflictos, JSONB, unicidad, repositorios, dependencias, migraciones y Outbox.

Las diferencias de implementación entre Smoke y xUnit, la cobertura porcentual y la ejecución en CI/CD permanecen fuera de las afirmaciones verificadas.

Este documento constituye evidencia técnica para la revisión del equipo antes de cualquier integración posterior.