# Reporte de Análisis: Telemetry API y Componentes Relacionados

A continuación se presenta el análisis de los tres componentes (repositorios/carpetas) identificados: **Backend** (`backend/Telemetry.Api`), **Pruebas** (`tests/Telemetry.Api.Tests`), y **Despliegue** (`deploy`).

## 1. Backend (Telemetry.Api)

Es una Minimal API en .NET 8 que utiliza EF Core con Oracle.

### Problemas y Fallas Identificadas
*   **Arquitectura Monolítica en `Program.cs`:** El archivo `Program.cs` contiene toda la configuración, lógica de pipeline y manejadores de endpoints. Esto dificulta la lectura, el mantenimiento y las pruebas unitarias aisladas. Viola el principio de responsabilidad única.
*   **Rate Limiting en Memoria:** La implementación de `RateLimiter` utiliza una política `FixedWindowLimiter` local. En un entorno escalado horizontalmente (múltiples contenedores en ECS), el límite se aplica por instancia, no globalmente. Esto hace que la limitación sea inconsistente y potencialmente ineficaz.
*   **Migraciones de Base de Datos en el Arranque:** La aplicación ejecuta `dbCtx.Database.Migrate()` al iniciar (`Program.cs`). En entornos distribuidos con múltiples réplicas arrancando simultáneamente, esto puede causar condiciones de carrera (race conditions) y errores de bloqueo en la base de datos.
*   **Modelo de Dominio Anémico:** La clase `TelemetryEvent` es solo un contenedor de datos (DTO disfrazado de entidad). Aunque aceptable para CRUD simple, no encapsula reglas de negocio.
*   **Strings Mágicos:** Uso de cadenas literales para nombres de conexión ("Db", "Oracle") y etiquetas de salud ("live", "ready"), lo que es propenso a errores tipográficos.
*   **Manejo de Errores:** Aunque hay un bloque `try-catch` global en los endpoints, devolver un 500 genérico con el mensaje de la excepción (`ex.Message`) puede exponer detalles internos sensibles o no ser útil para el cliente.

### Sugerencias
*   **Refactorizar `Program.cs`:** Extraer la configuración de servicios y middleware a métodos de extensión. Mover la lógica de los endpoints a clases dedicadas (usando patrones como REPR con bibliotecas como *FastEndpoints* o simplemente clases estáticas con métodos de extensión).
*   **Rate Limiting Distribuido:** Utilizar un almacén distribuido como Redis para contar las peticiones, o delegar el rate limiting a la capa de infraestructura (API Gateway / Load Balancer).
*   **Desacoplar Migraciones:** Mover la ejecución de migraciones a un "Job" de inicialización separado o un paso específico en el pipeline de CI/CD, antes de desplegar la nueva versión de la API.
*   **Mejorar Configuración:** Utilizar el patrón *Options* para inyectar configuraciones fuertemente tipadas.

## 2. Pruebas (Telemetry.Api.Tests)

Proyecto de pruebas de integración usando xUnit y `WebApplicationFactory`.

### Problemas y Fallas Identificadas
*   **Falsos Positivos con SQLite:** Las pruebas reemplazan el proveedor de base de datos Oracle por SQLite en memoria (`TelemetryApiFactory.cs`). SQLite no soporta todas las características de Oracle ni se comporta igual (diferencias en tipos de datos, sensibilidad a mayúsculas, sintaxis SQL específica). Una prueba que pasa en SQLite podría fallar en producción con Oracle.
*   **Cobertura Limitada:** Las pruebas en `BasicApiTests.cs` cubren casos de éxito básicos y una validación de error. Faltan pruebas para límites de concurrencia, problemas de conexión, y validación exhaustiva de tipos de datos.
*   **Limpieza de Datos:** Al usar una base de datos en memoria compartida, el estado de una prueba podría afectar a otras si no se gestiona correctamente el ciclo de vida o la limpieza (aunque `WebApplicationFactory` suele manejar esto bien si se configura por test).

### Sugerencias
*   **Usar Testcontainers:** Reemplazar SQLite por **Testcontainers for .NET** con una imagen de Oracle XE. Esto permitirá ejecutar las pruebas contra una base de datos real idéntica a la de producción, garantizando la fidelidad de las pruebas.
*   **Organización de Pruebas:** Separar las pruebas en diferentes clases según la funcionalidad (e.g., `TelemetryIngestionTests`, `TelemetryQueryTests`).
*   **Pruebas de Carga:** Añadir pruebas que verifiquen el comportamiento bajo carga para validar el Rate Limiter (aunque esto suele ser mejor en pruebas de sistema separadas).

## 3. Despliegue (deploy)

Scripts y configuraciones para despliegue en AWS ECS Fargate.

### Problemas y Fallas Identificadas
*   **Configuración Manual y Frágil:** Los archivos JSON (`task-definition.json`) contienen marcadores de posición (`<ACCOUNT_ID>`, `<ORACLE_CONNSTRING>`) que requieren sustitución manual o scripts propensos a errores (`sed`/`envsubst`).
*   **Seguridad:** Existe el riesgo de comprometer secretos (como la cadena de conexión) si se hardcodean en el `task-definition.json` o se suben al control de versiones por error.
*   **Falta de Infraestructura como Código (IaC):** Se utilizan scripts imperativos (`aws ecs register-task-definition`) y archivos JSON crudos. Esto dificulta la reproducibilidad, el versionado de la infraestructura y la detección de cambios (drift detection).
*   **Variables de Entorno Hardcodeadas:** Variables como `ASPNETCORE_URLS` están fijas en el JSON, reduciendo la flexibilidad.

### Sugerencias
*   **Adoptar IaC:** Utilizar herramientas modernas de Infraestructura como Código como **Terraform**, **AWS CDK** o **Pulumi**. Esto permite definir la infraestructura de manera programática, segura y reproducible.
*   **Gestión de Secretos:** No inyectar cadenas de conexión directamente en las variables de entorno de la definición de tarea. Utilizar referencias a **AWS Secrets Manager** o **AWS Systems Manager Parameter Store** directamente en la definición del contenedor.
*   **Automatización CI/CD:** Integrar el despliegue completamente en GitHub Actions, eliminando los pasos manuales descritos en el README.
