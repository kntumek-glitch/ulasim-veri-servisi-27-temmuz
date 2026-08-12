import requests
import concurrent.futures
import time
import json
import psutil
import threading
import statistics
import sys
from datetime import datetime

# SCENARIOS
# 0-transfer: Bornova Metro (38.459, 27.224) to Ege University (38.456, 27.234)
# 1-transfer: Buca (38.384, 27.170) to Karsiyaka (38.457, 27.114)
# 2-transfer: Urla (38.322, 26.764) to Bornova Metro (38.459, 27.224)
SCENARIOS = {
    "0-Transfer": {"fromLat": 38.459, "fromLon": 27.224, "toLat": 38.456, "toLon": 27.234},
    "1-Transfer": {"fromLat": 38.384, "fromLon": 27.170, "toLat": 38.457, "toLon": 27.114},
    "2-Transfer": {"fromLat": 38.322, "fromLon": 26.764, "toLat": 38.459, "toLon": 27.224},
}

MODES = ["DEPART_AT", "ARRIVE_BY"]
CONCURRENCY_TIERS = [1, 10, 25, 50]
DURATION_SEC = 2
URL = "http://localhost:5108/api/v2/journey-plans/search"
DATETIME_STR = "2026-08-12T10:00:00+03:00"

def find_server_process():
    for p in psutil.process_iter(['pid', 'name', 'cmdline']):
        try:
            if 'ulasim-veri-servisi.exe' in p.info['name']:
                return psutil.Process(p.info['pid'])
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            continue
    return None

class MonitorThread(threading.Thread):
    def __init__(self, process):
        super().__init__()
        self.process = process
        self.keep_running = True
        self.cpu_samples = []
        self.mem_samples = []
        self.daemon = True
    def run(self):
        try:
            self.process.cpu_percent(interval=None) # First call returns 0
            while self.keep_running:
                self.cpu_samples.append(self.process.cpu_percent(interval=0.1))
                self.mem_samples.append(self.process.memory_info().rss)
        except Exception:
            pass

def run_worker(payload):
    start = time.time()
    try:
        resp = requests.post(URL, json=payload, timeout=15)
        elapsed = time.time() - start
        if resp.status_code == 200:
            data = resp.json()
            inner_ms = data.get("metadata", {}).get("internalCalculationMs", 0)
            return (True, elapsed * 1000, inner_ms)
        else:
            return (False, elapsed * 1000, 0)
    except Exception as e:
        return (False, (time.time() - start) * 1000, 0)

def main():
    print("Finding server process for resource monitoring...")
    server_process = find_server_process()
    if not server_process:
        print("WARNING: Could not find ulasim-veri-servisi.exe process. CPU/Memory metrics will be 0.")

    results = []

    print(f"{'Mode':<12} | {'Scenario':<12} | {'C':<3} | {'RPS':<6} | {'Err%':<5} | {'VecB (p50/p95/p99) ms':<22} | {'VecA (p50/p95/p99) ms':<22} | {'CPU%':<5} | {'Mem MB':<7}")
    print("-" * 115)

    for mode in MODES:
        for scen_name, scen_coords in SCENARIOS.items():
            for c in CONCURRENCY_TIERS:
                payload = {
                    "origin": { "lat": scen_coords["fromLat"], "lon": scen_coords["fromLon"] },
                    "destination": { "lat": scen_coords["toLat"], "lon": scen_coords["toLon"] },
                    "dateTime": DATETIME_STR,
                    "searchMode": 0 if mode == "DEPART_AT" else 1,
                    "maxResults": 3
                }

                monitor = MonitorThread(server_process) if server_process else None
                if monitor:
                    monitor.start()

                vector_b_times = []
                vector_a_times = []
                errors = 0
                total_reqs = 0

                start_time = time.time()
                
                with concurrent.futures.ThreadPoolExecutor(max_workers=c) as executor:
                    futures = set()
                    
                    # initial batch
                    for _ in range(c):
                        futures.add(executor.submit(run_worker, payload))
                    
                    while time.time() - start_time < DURATION_SEC:
                        done, futures = concurrent.futures.wait(futures, return_when=concurrent.futures.FIRST_COMPLETED, timeout=0.5)
                        for fut in done:
                            total_reqs += 1
                            succ, b_ms, a_ms = fut.result()
                            if succ:
                                vector_b_times.append(b_ms)
                                vector_a_times.append(a_ms)
                            else:
                                errors += 1
                            if time.time() - start_time < DURATION_SEC:
                                futures.add(executor.submit(run_worker, payload))
                
                real_duration = time.time() - start_time
                if monitor:
                    monitor.keep_running = False
                    monitor.join(timeout=1.0)

                rps = total_reqs / real_duration if real_duration > 0 else 0
                err_rate = (errors / total_reqs * 100) if total_reqs > 0 else 0

                def get_pct(lst, p):
                    if not lst: return 0.0
                    return statistics.quantiles(lst, n=100, method='inclusive')[p-1] if len(lst) > 1 else lst[0]

                b_p50 = get_pct(vector_b_times, 50)
                b_p95 = get_pct(vector_b_times, 95)
                b_p99 = get_pct(vector_b_times, 99)

                a_p50 = get_pct(vector_a_times, 50)
                a_p95 = get_pct(vector_a_times, 95)
                a_p99 = get_pct(vector_a_times, 99)

                avg_cpu = sum(monitor.cpu_samples) / len(monitor.cpu_samples) if monitor and monitor.cpu_samples else 0.0
                max_mem = max(monitor.mem_samples) / (1024*1024) if monitor and monitor.mem_samples else 0.0

                print(f"{mode:<12} | {scen_name:<12} | {c:<3} | {rps:<6.1f} | {err_rate:<5.1f} | {b_p50:>5.0f}/{b_p95:>5.0f}/{b_p99:>5.0f}        | {a_p50:>5.0f}/{a_p95:>5.0f}/{a_p99:>5.0f}        | {avg_cpu:<5.1f} | {max_mem:<7.1f}")
                
                results.append({
                    "mode": mode,
                    "scenario": scen_name,
                    "concurrency": c,
                    "requests": total_reqs,
                    "errors": errors,
                    "rps": rps,
                    "vectorB": {"p50": b_p50, "p95": b_p95, "p99": b_p99},
                    "vectorA": {"p50": a_p50, "p95": a_p95, "p99": a_p99},
                    "cpu": avg_cpu,
                    "memoryMB": max_mem
                })

    with open("docs/load_test_results.json", "w") as f:
        json.dump(results, f, indent=2)
    print("Done! Results saved to docs/load_test_results.json")

if __name__ == "__main__":
    main()
