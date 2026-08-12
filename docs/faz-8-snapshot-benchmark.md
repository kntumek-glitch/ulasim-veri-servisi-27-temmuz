# Phase 8: Routing Snapshot Performance Benchmark (Real GTFS Dataset)

## Overview

This document outlines the performance characteristics of the V2 `RoutingSnapshotManager` when operating on the real, active Izmir ESHOT GTFS dataset. The benchmarking was performed on the `Release` build of the server to capture authentic memory allocations and execution durations during the cold-start and background snapshot reload cycles.

## 1. Build Duration & Cold Start

- **Snapshot Build Duration:** **8,089 ms (~8.1 seconds)**
- **Cold Start Time:** ~8.1 seconds to "Snapshot Ready"

*Note: The system blocks V2 Journey Plan searches until the first snapshot is generated during warmup, making cold start directly tied to the snapshot build duration.*

## 2. Memory Footprint

- **Peak Memory Usage (During Build):** **2,133,819,392 bytes (~2.03 GB)**
  *Analysis: EF Core tracking, large list allocations for `GtfsStopTimes` (2.2 million rows), and dictionary creation cause significant garbage collection pressure during the build phase.*
- **Final Estimated In-Memory Footprint:** **53,560,200 bytes (~51 MB)**
  *Analysis: Once the snapshot arrays (`StopIdToIndex`, `StopTimes`, `Trips`, `Patterns`) are finalized and the EF Core context is disposed, the long-lived memory requirement is extremely lean.*

## 3. Topology Scale

The captured snapshot reflects the size of the active Izmir public transport network:

- **Stop Count:** 11,510
- **Pattern (Route) Count:** 847
- **Trip Count:** 65,012
- **Stop-Time Count:** 2,216,478
- **Transfer Edge Count:** 0 *(Note: Transfer generation is currently inactive or not yielding edges in the active run)*

## Recommendations & Next Steps

1. **Memory Optimization:** While the final footprint is excellent (51 MB), the peak memory of ~2 GB during the build might crash constrained environments (e.g., small Docker containers). Consider using `AsNoTracking()` in EF Core or streaming records in batches rather than `.ToList()` to reduce peak allocations.
2. **Transfer Edges:** Investigate the 0 Transfer Edge Count. Footpath generation algorithms between nearby stops may need to be executed to enable robust multi-modal routing.
