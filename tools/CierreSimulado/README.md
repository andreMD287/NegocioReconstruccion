# CierreSimulado

Servidor gRPC para la demostracion de Reconstruccion.

Implementa las operaciones del contrato cierre.proto:

- NotificarPlanCompletado
- NotificarReapertura

No sustituye el sistema Cierre real ni es apto para produccion.

## Ejecutar en Windows

Desde la raiz del repositorio:

    dotnet run --project tools/CierreSimulado/CierreSimulado.csproj

Puertos:

- gRPC: http://localhost:56051
- Administracion: http://localhost:56052

## Controlar los modos

Ejecutar en otra consola PowerShell:

    Invoke-RestMethod http://localhost:56052/admin/estado

    Invoke-RestMethod -Method Post http://localhost:56052/admin/modo/aceptar

    Invoke-RestMethod -Method Post http://localhost:56052/admin/modo/rechazar

    Invoke-RestMethod -Method Post http://localhost:56052/admin/modo/no-disponible

    Invoke-RestMethod http://localhost:56052/admin/notificaciones

## Comportamiento

aceptar:
Registra la notificacion y devuelve Aceptada=true.

rechazar:
Devuelve Aceptada=false con un motivo.

no-disponible:
Responde con codigo gRPC UNAVAILABLE.

Una notificacion ya registrada vuelve a aceptarse sin duplicarse.

Los registros se guardan en JSONL y se recuperan al reiniciar.
La clave de deduplicacion contiene operacion, desastre y version.

El modo no-disponible simula una falla de gRPC.
Para demostrar una caida real, detener el proceso o contenedor.

## Persistencia

Variable opcional:

    CIERRE_SIMULADO_ARCHIVO

## Docker

Para habilitar el puerto administrativo dentro de un contenedor:

    CIERRE_SIMULADO_ADMIN_CONTENEDOR=1

Cuando se utilice Docker Compose, publicar el puerto administrativo
unicamente en 127.0.0.1 del equipo anfitrion.

Los endpoints administrativos no tienen autenticacion.
Usar exclusivamente en un entorno local de demostracion.

La aceptacion representa registro de la notificacion,
no el cierre formal del desastre.