# Journey Planning V2 - Standard Operating Procedure (SOP) & Runbook

This document provides backend engineers with actionable diagnostic and mitigation steps for 10 specific incident scenarios regarding the V2 Raptor Routing Engine and the GTFS data pipeline.

---

## 1. Data Pipeline & State

### 1.1 GTFS Import Fails
**Diagnosis:**
The scheduled or triggered GTFS import fails to process the zip file. Check the system logs or query the DB for failed runs.
```sql
-- Check the latest failed imports and their error messages
SELECT id, source_url, started_at, error_message 
FROM "GtfsImportRuns" 
WHERE status = 'FAILED' 
ORDER BY started_at DESC LIMIT 5;
```
**Mitigation:**
1. Check if the external GTFS source URL is accessible (`curl -I <gtfs_url>`).
2. If it was a network timeout, manually trigger a new import using the Admin API:
```bash
curl -X POST https://api.domain.com/api/v1/admin/gtfs/import \
     -H "Authorization: Bearer <ADMIN_KEY>"
```

### 1.2 Candidate Snapshot Build Fails
**Diagnosis:**
The GTFS data was imported into SQL, but building the in-memory `RoutingSnapshot` failed (often due to OOM or bad data relationships).
```sql
-- Identify runs that are stuck at DB import but failed snapshot generation
SELECT id, status, error_message 
FROM "GtfsImportRuns" 
WHERE status = 'COMPLETED_IMPORT' AND is_active = false;
```
**Mitigation:**
1. Check available server RAM (`free -h`). If OOM, increase Docker/VM memory limits.
2. Restart the application service to re-trigger the `SnapshotWarmupService` for pending runs.
```bash
systemctl restart ulasim-veri-servisi
```

### 1.3 Active Snapshot is Missing
**Diagnosis:**
The Journey Planning API returns `FEED_NOT_AVAILABLE` (HTTP 503). This means the graph is not loaded into memory.
**Mitigation:**
1. Check if an active feed exists in the database:
```sql
SELECT id FROM "GtfsImportRuns" WHERE is_active = true;
```
2. If it exists but is not loaded into memory, force a memory reload by restarting the application instance.
```bash
docker restart ulasim-api
```

### 1.4 Active Feed is Marked as Stale
**Diagnosis:**
The Journey Planning API returns `FEED_STALE` and routes are rejected because the search date exceeds the GTFS calendar. Response metadata shows `"isFeedStale": true`.
**Mitigation:**
1. The GTFS calendar has expired. A fresh GTFS file must be imported immediately.
2. Trigger an emergency import, ensuring the source URL points to the updated file:
```bash
curl -X POST https://api.domain.com/api/v1/admin/gtfs/import \
     -H "Authorization: Bearer <ADMIN_KEY>"
```

---

## 2. Infrastructure & External Dependencies

### 2.1 PostgreSQL Database is Unreachable (Down)
**Diagnosis:**
API endpoints return `500 Internal Server Error`. The application logs show `NpgsqlException: Connection refused`.
**Mitigation:**
1. Verify the database service status:
```bash
sudo systemctl status postgresql
# Or if running in Docker: docker ps | grep postgres
```
2. Restart the database service:
```bash
sudo systemctl restart postgresql
```
3. Verify connection locally from the application server:
```bash
psql -h localhost -U postgres -d TransportDb -W
```

### 2.2 External Walking Routing Provider is Down
**Diagnosis:**
API logs show timeout exceptions (`TaskCanceledException`) originating from `OsrmWalkingRouteProvider`. Walking routes fail to generate.
**Mitigation:**
1. Test the current OSRM endpoint directly:
```bash
curl -I http://router.project-osrm.org/route/v1/foot/27.1,38.4;27.2,38.5
```
2. If the primary OSRM server is down, update `appsettings.json` to point to a fallback instance or increase the timeout limit:
```json
"Osrm": { 
  "BaseUrl": "http://fallback-osrm.com", 
  "TimeoutSeconds": 10 
}
```
3. Restart the application to apply the new configuration.

---

## 3. Runtime & Performance

### 3.1 V2 Journey Planning Endpoint Timeouts
**Diagnosis:**
Clients receive `408 Request Timeout` and `SEARCH_TIMEOUT` metadata codes due to prolonged Raptor graph traversal.
**Mitigation:**
1. Inspect CPU load and active threads (`htop`).
2. If caused by an automated burst, lower the rate limiting thresholds in `appsettings.json` to aggressively throttle traffic:
```json
"RateLimit": {
    "PermitLimit": 20,
    "WindowSeconds": 10
}
```
3. If heavy queries are the root cause, temporarily reduce the fail-fast timeout threshold to free up threads:
```json
"JourneyPlan": { 
    "MaxSearchTimeSeconds": 5 
}
```

### 3.2 High Memory Consumption (OOM Risk)
**Diagnosis:**
The application process RAM usage exceeds critical thresholds (e.g., >80%). Logs might show high Garbage Collection overhead.
**Mitigation:**
1. The `WalkingRoutingCache` might be hoarding memory. Restart the service to flush the in-memory dictionaries immediately:
```bash
systemctl restart ulasim-veri-servisi
```
2. To prevent recurrence, reduce the maximum cache capacity in `appsettings.json`:
```json
"WalkingRoutingCache": { 
    "MaxCapacity": 2000 
}
```

---

## 4. Disaster Recovery

### 4.1 Rollback Procedure for a Newly Promoted, Faulty Feed
**Diagnosis:**
A new GTFS feed was imported and set to active, but clients are reporting missing routes, zero trips, or incorrect topologies.
**Mitigation:**
1. Identify the ID of the previous stable run:
```sql
SELECT id, started_at FROM "GtfsImportRuns" 
WHERE status = 'COMPLETED_ALL' 
ORDER BY id DESC LIMIT 5;
```
2. Trigger the manual activation of the previous known-good run via the API (which automatically deactivates the faulty one):
```bash
curl -X POST https://api.domain.com/api/v1/admin/gtfs/activate/{STABLE_RUN_ID} \
     -H "Authorization: Bearer <ADMIN_KEY>"
```

### 4.2 Revert Procedure to a Previous Stable Snapshot (DB Level)
**Diagnosis:**
The active feed is corrupt and the API is entirely unresponsive, preventing the use of the Admin API for rollback.
**Mitigation:**
1. Stop the API service to stop serving corrupt data:
```bash
systemctl stop ulasim-veri-servisi
```
2. Directly intervene in the SQL database to swap the active flags:
```sql
-- Step 1: Deactivate the current faulty run
UPDATE "GtfsImportRuns" SET is_active = false WHERE is_active = true;

-- Step 2: Activate the previous stable run (replace 12 with the stable ID)
UPDATE "GtfsImportRuns" SET is_active = true WHERE id = 12;
```
3. Restart the API service. The `SnapshotWarmupService` will rebuild the memory graph using the newly marked stable run:
```bash
systemctl start ulasim-veri-servisi
```
