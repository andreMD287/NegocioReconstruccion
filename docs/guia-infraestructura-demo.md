# Guía de infraestructura de demostración — NegocioReconstruccion

## 1. Objetivo

Esta guía permite reproducir la infraestructura de demostración del proyecto NegocioReconstruccion.

Componentes, todos ejecutados en contenedores Docker:

- Host desarrollado en .NET 10 (gRPC, SignalR y autenticación JWT).
- PostgreSQL 16.
- Servicio gRPC CierreSimulado.

Y un cliente externo, que se ejecuta fuera de Docker porque representa a un sistema cliente real (igual que Caracterización o un operador):

- ClienteDemo para ejecutar el Escenario E.

Capacidades cubiertas por la infraestructura:

- Comunicación en tiempo real mediante SignalR.
- Procesamiento de eventos mediante Outbox.
- Persistencia mediante volúmenes Docker.

Esta infraestructura está diseñada exclusivamente para desarrollo, pruebas y sustentación académica.

## 2. Requisitos

Se necesita:

- .NET SDK 10 (solo para ejecutar ClienteDemo; el Host se compila y corre dentro de su propio contenedor).
- Docker Desktop con Docker Compose.
- PowerShell en Windows.
- Repositorio NegocioReconstruccion.

Ejecutar los comandos desde la raíz del repositorio.

## 3. Generar credenciales locales

La contraseña de PostgreSQL no se almacena en Git.

Antes de iniciar por primera vez, ejecutar en PowerShell:

    if (-not (Test-Path ".env.demo.local")) {
        $bytes = New-Object byte[] 24
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

        try {
            $rng.GetBytes($bytes)
        }
        finally {
            $rng.Dispose()
        }

        $password = [BitConverter]::ToString($bytes).Replace("-", "")

        "RECONSTRUCCION_DEMO_DB_PASSWORD=$password" |
            Set-Content -Path ".env.demo.local" -Encoding Ascii
    }

Importante:

- No subir .env.demo.local a GitHub.
- No compartir la contraseña en documentos o capturas.
- No regenerar la contraseña si PostgreSQL ya fue inicializado.
- Conservar el archivo local mientras se utilice el volumen existente.

## 4. Iniciar Docker Compose

Ejecutar:

    docker compose --env-file .env.demo.local up -d --build --wait

Verificar:

    docker compose --env-file .env.demo.local ps

Servicios locales:

| Componente | Dirección |
|---|---|
| PostgreSQL 16 | 127.0.0.1:55435 |
| CierreSimulado gRPC | http://127.0.0.1:56051 |
| Administración de Cierre | http://127.0.0.1:56052 |
| Host HTTPS (gRPC y SignalR) | https://localhost:56050 |

Los tres componentes corren en contenedores propios; este único comando los compila y arranca. Los puertos publicados están restringidos a la interfaz local de Windows.

## 5. Verificar los contenedores

Comprobar PostgreSQL:

    docker compose --env-file .env.demo.local exec -T postgres pg_isready -U reconstruccion_demo -d reconstruccion_demo

Comprobar CierreSimulado:

    Invoke-RestMethod "http://127.0.0.1:56052/salud"

Consultar el estado:

    Invoke-RestMethod "http://127.0.0.1:56052/admin/estado"

Comprobar el Host:

    Invoke-RestMethod "https://localhost:56050/" -SkipCertificateCheck

PostgreSQL y CierreSimulado deben responder correctamente, y el Host debe devolver el mensaje de servicios gRPC y SignalR. El Host usa un certificado HTTPS autofirmado generado dentro de su propia imagen (no el certificado de desarrollo de Windows); por eso esta comprobación necesita `-SkipCertificateCheck`, igual que ClienteDemo, que tampoco valida la cadena de confianza del certificado.

## 6. Migraciones de Entity Framework Core

El Host las aplica automáticamente al iniciar (variable `Migraciones__AplicarAlIniciar` en `docker-compose.yml`); no es necesario ningún paso manual para la demostración.

Para aplicarlas o revisarlas manualmente desde Windows (por ejemplo, para inspeccionar el esquema sin levantar el Host), restaurar las herramientas locales:

    dotnet tool restore

Leer la contraseña del entorno:

    $lineas = @(
        Get-Content ".env.demo.local" |
            Where-Object {
                $_ -match '^RECONSTRUCCION_DEMO_DB_PASSWORD=[A-Fa-f0-9]{48}$'
            }
    )

    if ($lineas.Count -ne 1) {
        throw "No se encontro una contrasena local valida."
    }

    $password = $lineas[0].Split("=", 2)[1]

Construir la conexión:

    $connection = "Host=127.0.0.1;Port=55435;Database=reconstruccion_demo;Username=reconstruccion_demo;Password=$password"

La fábrica ReconstruccionDbContextFactory utiliza RECONSTRUCCION_DB_CONNECTION.

Aplicar las migraciones existentes:

    $anterior = [Environment]::GetEnvironmentVariable(
        "RECONSTRUCCION_DB_CONNECTION", "Process"
    )

    try {
        $env:RECONSTRUCCION_DB_CONNECTION = $connection

        dotnet ef database update --project src/Reconstruccion.Infraestructura/Reconstruccion.Infraestructura.csproj --startup-project src/Reconstruccion.Infraestructura/Reconstruccion.Infraestructura.csproj --context ReconstruccionDbContext

        if ($LASTEXITCODE -ne 0) {
            throw "Fallaron las migraciones."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            "RECONSTRUCCION_DB_CONNECTION", $anterior, "Process"
        )
    }

Migraciones existentes:

1. InicialReconstruccion.
2. UnicidadEvaluacionPorPlan.
3. RegistrosTransaccionales.
4. OrdenOutboxPorDesastre.

Las migraciones aplicadas quedan registradas en __EFMigrationsHistory.

No generar nuevamente las migraciones.

## 7. El Host

El Host ya quedó en ejecución desde el paso 4, dentro de su propio contenedor, escuchando HTTPS en el puerto publicado 56050. No requiere ninguna variable de entorno ni comando adicional en Windows: la cadena de conexión, la clave JWT y la dirección de CierreSimulado ya están configuradas como variables de entorno del servicio `host` en `docker-compose.yml`.

La configuración JWT de Development usada en la demo no debe reutilizarse en un entorno real; no compartir claves privadas ni reemplazar configuraciones existentes sin revisarlas.

El endpoint /dev/token es exclusivamente para pruebas locales en Development.

## 8. Ejecutar ClienteDemo

Con Docker y el Host funcionando, abrir otra terminal.

Ejecutar:

    dotnet run --no-launch-profile --project tools/ClienteDemo/Reconstruccion.ClienteDemo.csproj -- https://localhost:56050

El Escenario E realiza operaciones funcionales de reconstrucción.

Incluye:

- Recepción de necesidades desde Caracterización.
- Creación de proyectos.
- Registro del presupuesto.
- Asignación de participantes.
- Definición del cronograma.
- Aprobación e inicio del proyecto.
- Registro de avances.
- Registro de costos.
- Adjuntar documentos.
- Finalización y seguimiento del plan.
- Recepción de eventos SignalR.
- Comunicación con CierreSimulado.

En condiciones normales, el resultado esperado es EstadoPlanCerrado.

El cliente genera datos reales de demostración dentro de PostgreSQL.

## 9. Modos de CierreSimulado

Modo normal:

    Invoke-RestMethod -Method Post "http://127.0.0.1:56052/admin/modo/aceptar"

Simular rechazo:

    Invoke-RestMethod -Method Post "http://127.0.0.1:56052/admin/modo/rechazar"

Simular indisponibilidad:

    Invoke-RestMethod -Method Post "http://127.0.0.1:56052/admin/modo/no-disponible"

Consultar estado:

    Invoke-RestMethod "http://127.0.0.1:56052/admin/estado"

Consultar notificaciones:

    Invoke-RestMethod "http://127.0.0.1:56052/admin/notificaciones"

Después de demostrar una falla, restaurar siempre el modo aceptar.

El puerto administrativo debe permanecer restringido al entorno local de demostración.

## 10. Escenarios de fallo

### Fallo 1: proyecto con hitos incompletos

La operación FinalizarProyecto debe rechazarse cuando los hitos no alcanzaron el 100 %.

Regla RN-RE-12.

### Fallo 2: evaluación repetida

Si Caracterización reenvía una evaluación ya procesada, el sistema no debe duplicar la necesidad.

Regla RN-RE-18.

### Fallo 3: servicio Cierre no disponible

Procedimiento:

1. Activar el modo no-disponible.
2. Ejecutar ClienteDemo.
3. Comprobar que el plan permanece Completado.
4. Observar los reintentos del Outbox.
5. Restaurar Cierre al modo aceptar.
6. Comprobar que se entrega la notificación pendiente.

La entrega de eventos es al menos una vez.

Por esa razón pueden observarse notificaciones repetidas mediante SignalR.

Los consumidores deben implementar procesamiento idempotente.

## 11. Comprobar el Outbox

Consultar eventos pendientes desde PowerShell:

    $sql = 'SELECT COUNT(*) FROM eventos_outbox WHERE "PublicadoEn" IS NULL;'

    $sql | docker compose --env-file .env.demo.local exec -T postgres psql -X -A -t -v ON_ERROR_STOP=1 -U reconstruccion_demo -d reconstruccion_demo

Un resultado de cero indica que, en el instante de la consulta, no existen eventos pendientes de publicación.

No significa que nunca puedan existir nuevos eventos pendientes.

## 12. Persistencia

Los contenedores utilizan dos volúmenes:

- postgres_datos: información de PostgreSQL.
- cierre_datos: registro de notificaciones aceptadas.

Los volúmenes sobreviven al reinicio ordinario de los contenedores.

No ejecutar docker compose down -v durante la demostración.

No borrar los volúmenes sin autorización del equipo.

## 13. Evidencias de validación

Validaciones realizadas sobre la infraestructura completa (Docker Compose, PostgreSQL 16 y CierreSimulado reales):

- CierreSimulado compilado y publicado.
- Pruebas gRPC de aceptación, rechazo, idempotencia e indisponibilidad.
- Persistencia de notificaciones comprobada tras reinicio.
- PostgreSQL 16 iniciado correctamente y cuatro migraciones aplicadas.
- Host iniciado con HTTPS.
- ClienteDemo ejecutado contra PostgreSQL y CierreSimulado reales; Escenario E terminado con EstadoPlanCerrado.
- Notificación de cierre registrada.
- Pruebas de hitos incompletos y evaluación duplicada.
- Caída de Cierre simulada; plan conservado en Completado durante la indisponibilidad.
- Notificación recuperada después de restaurar Cierre.
- Outbox verificado con cero eventos pendientes.
- Estado Cerrado confirmado, tras la recuperación, mediante una consulta gRPC independiente (un cliente y un token distintos de los que generaron los datos), no solo mediante los eventos SignalR de la misma sesión de demostración.
- Host compilado y ejecutado dentro de su propio contenedor (no en Windows), con migraciones aplicadas automáticamente al iniciar: el Escenario E completo, incluidas las tres pruebas de fallo, se repitió contra esa versión totalmente contenerizada con el mismo resultado.

## 14. Recomendaciones para la sustentación

Antes de iniciar:

1. Verificar Docker Compose.
2. Verificar PostgreSQL.
3. Restaurar CierreSimulado al modo aceptar.
4. Comprobar que el Host escucha por HTTPS.
5. Ejecutar ClienteDemo.
6. Presentar los escenarios de fallo.
7. Restaurar CierreSimulado.
8. Consultar las notificaciones y el Outbox.

No utilizar credenciales de producción.

No exponer los endpoints administrativos ni los tokens de desarrollo a redes públicas.

No modificar o eliminar la información persistida sin autorización.

## 15. Referencias técnicas del proyecto

- contracts/proto/reconstruccion/v1/cierre.proto
- tools/CierreSimulado
- tools/ClienteDemo
- src/Reconstruccion.Host
- src/Reconstruccion.Infraestructura/Persistencia/Migraciones
- tests/Reconstruccion.Integracion.Tests/PuntaAPunta
- docker-compose.yml