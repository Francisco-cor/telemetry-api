# 🛰️ Telemetry API — .NET 8 + Oracle EF Core + AWS Fargate

**Goal:** Provide a production-ready telemetry ingestion and query API with a lightweight, containerized architecture.

---

## 🚀 Stack

| Layer | Technology | Description |
|-------|-------------|-------------|
| Backend | ASP.NET Core Minimal API (.NET 8), EF Core (Oracle), FluentValidation | REST API for ingesting/querying telemetry events |
| Database | Oracle XE 21c | Stores time-series telemetry data |
| DevOps | Docker, ECS Fargate, GitHub Actions, GHCR | CI/CD pipeline with semantic tagging and deploy gating |

---

## ⚙️ Run Locally

### Backend
```bash
docker compose up --build
# → API available at http://localhost:5080
```

### Health Checks
```bash
curl http://localhost:5080/health
curl http://localhost:5080/health/db
```

### Sample Query
```bash
curl "http://localhost:5080/api/telemetry?source=Tractor-001&page=1&pageSize=10"
```

---

## 📡 API Endpoints

| Method | Endpoint | Description |
|---------|-----------|-------------|
| `GET` | `/health` | Service heartbeat |
| `GET` | `/health/db` | Database connectivity check |
| `POST` | `/api/telemetry` | Batch ingestion of telemetry events |
| `GET` | `/api/telemetry?source=&startDate=&endDate=&page=&pageSize=` | Filtered and paginated query |

### Example Payload
```json
{
  "events": [
    { "timestamp": "2025-11-08T08:00:00Z", "source": "Tractor-001", "metricName": "EngineRPM", "metricValue": 1900 },
    { "timestamp": "2025-11-08T08:01:00Z", "source": "Tractor-001", "metricName": "FuelRate", "metricValue": 2.6 }
  ]
}
```

---

## 🧩 Architecture

```mermaid
flowchart LR
  iot[Client / IoT Device] -->|HTTPS batch JSON| api[Telemetry API (.NET 8)]
  api -->|EF Core / Oracle.ManagedDataAccess| db[(Oracle XE DB)]
  api -. Logs JSON .-> cw[(CloudWatch / Serilog Console Sink)]
```

---

## 🧠 Design Decisions

- Minimal API for low boilerplate and easy testability.  
- FluentValidation ensures reliable schema validation for ingestion.  
- Serilog JSON structured logging → integrates with CloudWatch Insights.  
- Docker Compose enables local parity with ECS Fargate environment.  
- GHCR + GitHub Actions handle versioned image publishing.  
- Index `(Source, Timestamp)` accelerates range queries for time-series data.  

---

## 🧱 Database Schema

```sql
CREATE TABLE TelemetryEvent (
  Id RAW(16) PRIMARY KEY,
  Timestamp TIMESTAMP NOT NULL,
  Source NVARCHAR2(100) NOT NULL,
  MetricName NVARCHAR2(100) NOT NULL,
  MetricValue FLOAT
);

CREATE INDEX IX_Telemetry_Source_Timestamp
  ON TelemetryEvent(Source, Timestamp);
```

---

## 🧪 Tests

| Suite | Framework | Purpose |
|--------|------------|----------|
| Backend | xUnit | Validates API endpoints (POST/GET) |
| Validation | FluentValidation | Ensures payload schema integrity |
| CI | GitHub Actions | Runs on every PR and push |

To run manually:
```bash
dotnet test ./tests/Telemetry.Api.Tests --verbosity normal
```

---

## 🧰 CI/CD Pipeline

**Workflows:**
1. **ci-cd.yml** → restore, lint, test, build, push to GHCR  
2. **deploy-ecs.yml** → optional AWS ECS deploy (runs only if tag + AWS secrets exist)

**Tagging logic:**
- Commits → `v0.0.0-<sha>`  
- Releases → `vX.Y.Z`  

---

## 🩹 Rollback Plan (Preview)

1. Keep multiple image versions in GHCR for instant tag rollback.  
2. ECS policy → `minimumHealthyPercent = 100`, `maximumPercent = 200`.  
3. Manual rollback via AWS CLI:  
   ```bash
   aws ecs update-service      --cluster <CLUSTER>      --service <SERVICE>      --force-new-deployment      --task-definition <PREVIOUS_TASK_DEF_ARN>
   ```

---

## 🧭 Scalability

- Stateless API, horizontally scalable (ECS tasks or pods).  
- Oracle XE can migrate to RDS Oracle Standard easily.  
- JSON logs easily aggregated by CloudWatch Insights.  
- Designed for lightweight edge and cloud deployments.

---

## 🧾 CI Status

✅ Lint / Test / Build  
🕒 Deploy (skipped if no AWS credentials)

---

## 🪪 License

MIT — 2025 © Francisco Cordero Aguero

---

## 🧪 Testing Strategy

Integration tests (xUnit + WebApplicationFactory + SQLite in-memory) run automatically on CI to validate all endpoints:
- ✅ **POST /api/telemetry** → 201 Created, batch insert validated
- ✅ **GET /api/telemetry** → 200 OK, pagination and filters
- 🧱 **/health/live** and **/health/ready** verify DB connectivity and readiness

Local run:
```bash
dotnet test ./tests/Telemetry.Api.Tests --verbosity normal
```

---

## ⚡ Quickstart Summary

```bash
# 1. Build & run locally
docker compose up --build

# 2. Post sample telemetry
curl -X POST http://localhost:5080/api/telemetry -H "Content-Type: application/json"   -d '{"events":[{"timestamp":"2025-11-08T08:00:00Z","source":"T-001","metricName":"RPM","metricValue":1900}]}'

# 3. Query data
curl "http://localhost:5080/api/telemetry?source=T-001&page=1&pageSize=5"
```
