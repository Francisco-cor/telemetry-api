# Telemetry API — .NET 8 + Oracle (EF Core) + AWS Fargate Ready

## Objetivo
Servicio de ingesta/consulta de telemetría listo para producción mínima: batch POST, GET con filtros/paginación, índices adecuados, logging estructurado y CI/CD con imágenes versionadas.

## Arquitectura
```mermaid
flowchart LR
  iot[Dispositivo/Cliente] -->|HTTPS batch JSON| api[Telemetry API (ECS Fargate)]
  api -->|ADO.NET/EF Core| db[(Oracle DB)]
  api -. logs JSON .-> cw[(CloudWatch Logs)]
```

## Modelo e Índices
Entidad principal: `TelemetryEvent(Id, Timestamp, Source, MetricName, MetricValue)`.
Índice compuesto **(Source, Timestamp)** para acelerar `WHERE Source = ? AND Timestamp BETWEEN ? AND ?`.

## Endpoints
- `POST /api/telemetry` — Ingresa **batch** de eventos (array).
- `GET /api/telemetry?source=&startDate=&endDate=&page=&pageSize=` — Consulta paginada y filtrada.

## Observabilidad
Logs JSON a consola → CloudWatch (driver `awslogs` en Fargate).

**CloudWatch Logs Insights (ejemplo):**
```
fields @timestamp, Source, MetricName, MetricValue
| filter MetricName = "EngineRPM" and Source = "Tractor-SN12345"
| sort @timestamp desc
| limit 50
```

## CI/CD
GitHub Actions: formato → tests → build → Docker a GHCR.
Tags: `vX.Y.Z` (si hay tag) y `SHA` corto (para trazabilidad).

## Despliegue (AWS Fargate)
Usar `/deploy/aws-fargate/task-definition.json` + `iam-policy.json`.
Variables: `<AWS_REGION>`, `<ACCOUNT_ID>`, `<ECR_OR_GHCR_IMAGE_TAG>`, `<ORACLE_CONNSTRING>`.

## Plan de Rollback (crítico)
1. Identificar imagen estable previa en GHCR/ECR (ej: `v1.2.2-f9e8d7c`).
2. Editar el servicio ECS para apuntar a ese tag:
   ```bash
   aws ecs update-service --cluster <CLUSTER> --service telemetry-api      --force-new-deployment --task-definition telemetry-api
   ```
3. Verificar salud y logs en CloudWatch.
