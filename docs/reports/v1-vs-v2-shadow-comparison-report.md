# Ulaşım Veri Servisi - V1 vs V2 Shadow Routing Comparison

## 1. Overview
The transition from Journey Planning V1 to V2 involved migrating from an abstract pathfinding approach to a highly specialized, timetabled RAPTOR (Round-Based Public Transit Routing) implementation. To ensure absolute data parity and reliability, a Shadow Routing testing strategy was implemented during Phase 8.

## 2. Shadow Routing Methodology
**Goal**: Verify that V2 Engine outputs are semantically equivalent or vastly superior to V1 Engine outputs without crashing, missing vital connections, or hallucinating invalid routes.
- The Golden 40 OD (Origin-Destination) Regression Suite served as the primary traffic generator.
- Simulated requests were structurally compared between the legacy `IJourneyPlanningService` endpoints and the new RAPTOR engine.

## 3. Key Findings

### Output Parity
- **Valid Routes**: V2 consistently generated geometrically and temporally valid routes that matched or exceeded V1's relevance.
- **Transfer Handling**: V1 relied on localized distance heuristics. V2 utilizes a strict pre-computed Transfer Matrix (GTFS `transfers.txt`), avoiding impossible transitions and accurately reflecting walking constraints.

### Response Metadata
- V1 responses were opaque regarding data freshness.
- **V2 Improvement**: V2 strictly enforces Temporal Constraints. It analyzes the GTFS Feed hash, generation timestamp, and checks `IsFeedStale`. This lineage metadata is now embedded directly in the API response headers/payloads.

### Error Handling & Bounding
- **Topological Bounding**: When queried for a service date outside the GTFS calendar (e.g., year 2099), V2 accurately returns a structured failure indicating "No active service," whereas V1 would often traverse aimlessly or crash.
- **Exception Sanitization**: Both V1 and V2 endpoints are now shielded by the Phase 8 Exception Masking Middleware, ensuring identical, secure `ProblemDetails` output on catastrophic internal failures.

## 4. Conclusion
The V2 Engine does not just replicate V1; it fundamentally improves upon it. By trading database roundtrips for in-memory graph traversals, V2 provides deterministic, strict timetabled outputs. The shadow comparison confirms that replacing V1 with V2 eliminates legacy inaccuracies while maintaining complete backwards compatibility for API consumers expecting standard itinerary structures.
