# NegocioReconstruccion

Módulo de Reconstrucción de la Plataforma para la Gestión de Desastres y Reconstrucción.

Implementa la capa de negocio completa (Dominio y Aplicación), su persistencia en PostgreSQL,
los adaptadores gRPC (servidor y cliente) y SignalR, la composición del Host en ASP.NET Core con
autenticación JWT, y un cliente de demostración externo que recorre el Escenario E de punta a
punta contra un servidor gRPC simulado de Cierre.

El diseño y las justificaciones de arquitectura están en el documento
`Proyecto_Reconstruccion_Capa_Negocio` (Google Docs del equipo).

## Tecnología

- C# y .NET 10, ASP.NET Core Minimal API.
- Clean Architecture con la regla de dependencias verificada por pruebas de arquitectura.
- gRPC (contratos en `contracts/proto/reconstruccion/v1`) y SignalR.
- PostgreSQL 16 y Entity Framework Core 10.
- JWT Bearer para autenticación; autorización contextual por rol (RN-RE-19).
- xUnit, con Testcontainers para las pruebas de integración y de punta a punta.
- Docker Compose para la infraestructura de demostración.

## Estructura de la solución

```
src/
  Reconstruccion.Dominio               Entidades, agregados y reglas de negocio (RN-RE-01..22).
  Reconstruccion.Aplicacion            Casos de uso, puertos, EjecutorOperaciones (idempotencia,
                                        auditoría, outbox) y autorización.
  Reconstruccion.Adaptadores.Grpc      Servidor y cliente gRPC; traducción de errores de negocio
                                        a códigos y trailers gRPC.
  Reconstruccion.Adaptadores.TiempoReal  Hub de SignalR y notificador.
  Reconstruccion.Infraestructura       EF Core, repositorios, outbox, publicador, lecturas.
  Reconstruccion.Host                  Composición de dependencias, JWT, mapeo de servicios
                                        gRPC y del hub, endpoint /dev/token (solo Development).
tools/
  ClienteDemo                          Cliente externo (solo contratos gRPC/SignalR) que corre
                                        el Escenario E y las pruebas de fallo.
  CierreSimulado                       Servidor gRPC simulado de Cierre con API de administración
                                        para alternar entre aceptar, rechazar y no disponible.
tests/
  Reconstruccion.Dominio.Tests         Reglas de negocio, sin infraestructura.
  Reconstruccion.Aplicacion.Tests      Casos de uso con puertos en memoria.
  Reconstruccion.Integracion.Tests     Persistencia y Host contra PostgreSQL real (Testcontainers),
                                        incluida la prueba de punta a punta.
docs/
  guia-infraestructura-demo.md         Guía completa, paso a paso, para levantar la demo con
                                        Docker Compose (PostgreSQL + CierreSimulado) y correrla.
```

Las dependencias entre capas siempre apuntan hacia adentro (Dominio no depende de nada del
proyecto; Aplicación depende solo de Dominio; los adaptadores e Infraestructura dependen de
Aplicación y Dominio); esto se verifica con `ReglaDeDependenciasTests` en cada proyecto de
pruebas.

## Compilar y probar

```
dotnet build NegocioReconstruccion.slnx
dotnet test NegocioReconstruccion.slnx
```

Las pruebas de integración y de punta a punta levantan su propio contenedor PostgreSQL con
Testcontainers; solo necesitan Docker disponible, no la infraestructura de `docker-compose.yml`.

## Ejecutar la demostración completa

La guía detallada —con CierreSimulado real, PostgreSQL persistente vía Docker Compose,
migraciones, y los tres escenarios de fallo— está en
[`docs/guia-infraestructura-demo.md`](docs/guia-infraestructura-demo.md).

En resumen: `docker compose --env-file .env.demo.local up -d --build --wait` levanta PostgreSQL
y CierreSimulado; se aplican las migraciones con `dotnet ef database update`; se inicia
`Reconstruccion.Host` en `Development` con `Cierre:Direccion` apuntando al CierreSimulado; y se
corre `dotnet run --project tools/ClienteDemo` contra el Host. El resultado esperado es que el
plan llegue a `Cerrado`.

## Estado

Capa de negocio, persistencia, adaptadores gRPC/SignalR, Host, demo y verificación de punta a
punta completos e integrados en `main`. La trazabilidad entre cada regla de negocio, la clase que
la implementa y la prueba que la verifica está en la sección 10 del documento de diseño.
