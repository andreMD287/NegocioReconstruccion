# NegocioReconstruccion

Modulo de Reconstruccion de la Plataforma para la Gestion de Desastres.

## Tecnologia

- C# y .NET 10
- ASP.NET Core
- Clean Architecture
- gRPC
- SignalR
- PostgreSQL y Entity Framework Core (previstos)

## Arquitectura

La solucion contiene seis proyectos:

- Reconstruccion.Dominio: entidades y reglas de negocio.
- Reconstruccion.Aplicacion: casos de uso y contratos.
- Reconstruccion.Adaptadores.Grpc: comunicacion gRPC.
- Reconstruccion.Adaptadores.TiempoReal: SignalR.
- Reconstruccion.Infraestructura: persistencia y servicios externos.
- Reconstruccion.Host: composicion y ejecucion.

Las dependencias apuntan hacia las capas internas.

## Estado

Estructura inicial. La implementacion funcional esta pendiente.

## Compilacion

dotnet build NegocioReconstruccion.slnx
