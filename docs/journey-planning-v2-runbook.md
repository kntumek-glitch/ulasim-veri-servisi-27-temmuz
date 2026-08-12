# Ulaşım Veri Servisi - V2 Engine Operations Runbook

## 1. Overview
This runbook provides comprehensive operational guidelines for managing the Ulaşım Veri Servisi V2 Routing Engine. The V2 Engine uses the RAPTOR algorithm optimized with binary search indices and strict state synchronization.

## 2. Health & Readiness Monitoring
The system exposes multiple endpoints to monitor the engine's health and snapshot readiness.

### `/health/live`
- **Purpose**: Liveness probe for orchestrators (e.g., Kubernetes).
- **Behavior**: Returns HTTP 200 immediately if the API process is running. Does not check database or snapshot states.

### `/health/ready`
- **Purpose**: Readiness probe. Used to verify if the V2 Routing Engine is fully initialized and safe to route traffic.
- **Validates**:
  1. PostgreSQL database connectivity.
  2. Applied EF Core migrations.
  3. External ESHOT API availability (returns HTTP 200 Degraded if down, but does not block traffic).
  4. GTFS Feed validity (Stale feeds older than 48 hours return HTTP 200 Degraded).
  5. V2 Engine Memory Snapshot integrity (Fails with HTTP 503 if snapshot is missing, mismatched with DB, or building).

### Admin Snapshot Endpoint
- **URL**: `GET /api/v2/admin/routing/snapshot`
- **Security**: Requires `x-admin-key` header matching the `Security:AdminKey` configuration value.
- **Response**: Provides exact memory blueprint telemetry:
  - `active_import_id`: The ID of the currently loaded feed.
  - `feed_hash`: SHA256 hash of the loaded GTFS feed.
  - `build_duration_ms`: Time taken to build the snapshot in memory.
  - `estimated_memory_bytes`: Approximate RAM footprint of the routing structures.

## 3. GTFS Import Lifecycle & Troubleshooting
The GTFS import process is automated and uses an atomic promotion model to prevent "split-brain" states.

### Process Flow
1. **Acquire Lock**: A PostgreSQL advisory lock (`123456`) ensures only one import runs simultaneously.
2. **Download & Check**: The `ETag` and `LastModified` headers are checked. If unchanged, the import skips.
3. **Truncate & Load**: Old GTFS tables are truncated. The new ZIP is parsed and loaded.
4. **Snapshot Generation**: A candidate routing snapshot is built in-memory.
5. **Atomic Promotion**: If the snapshot succeeds, the DB `GtfsImportRuns` status is marked as "Completed" and the active snapshot is swapped pointer-wise. If it fails, the transaction is rolled back and the old snapshot remains active.

### Troubleshooting Scenarios
#### Scenario 1: Import stuck in "Running" status
- **Cause**: The process crashed unexpectedly (e.g., OOM kill, pod eviction) before releasing the DB lock or updating the status.
- **Resolution**: The `GtfsImportService` will automatically detect abandoned runs during the next scheduled import and mark them as "Failed" with the message "Automatically marked as Failed (Abandoned)".

#### Scenario 2: Sequence Contains No Elements / Duplicate PK Errors
- **Cause**: A hardcoded Entity Framework ID collided with the PostgreSQL sequence generation.
- **Resolution**: Resolved in Phase 8. Ensure no explicit `Id` assignments exist in `GtfsImportRun` insertion logic.

#### Scenario 3: Memory Exhaustion (OOM) during Snapshot Build
- **Cause**: The GTFS feed size exceeded available system memory.
- **Resolution**: Check the `estimated_memory_bytes` in the Admin Endpoint for previous runs. If it approaches the container memory limit, increase the RAM allocation for the application pod.

## 4. Security Controls & Rate Limiting
### Exception Masking
All internal errors are masked to prevent stack trace leaks. The application uses a global Exception Middleware that catches all unhandled exceptions and returns a generic `ProblemDetails` response (RFC 7807) with HTTP 500.

### Rate Limiting
- Configured via `RateLimiting` section in `appsettings.json`.
- Limits traffic based on client IP or `X-Forwarded-For` headers.
- Breaches result in `HTTP 429 Too Many Requests`.

### CORS Policy
- Configured via `Cors:AllowedOrigins` in `appsettings.json`.
- Rejecting unauthorized origins is strictly enforced for web traffic.
