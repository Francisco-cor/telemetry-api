# Deploy rápido a ECS Fargate (resumen)

1) Construye y sube imagen (GHCR/ECR) vía CI.
2) Crea roles de `execution` y `task` con la `iam-policy.json` (o adjúntala al execution role).
3) Registra la definición de tarea:
   ```bash
   aws ecs register-task-definition --cli-input-json file://task-definition.json
   ```
4) Crea/actualiza el servicio en tu cluster:
   ```bash
   aws ecs update-service --cluster <CLUSTER> --service telemetry-api --task-definition telemetry-api
   ```
5) Logs en CloudWatch: `/ecs/telemetry-api`.
