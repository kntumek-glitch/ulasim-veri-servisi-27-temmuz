import json
import os

def generate_report():
    input_file = "docs/load_test_results.json"
    output_file = "docs/load_test_report.md"

    if not os.path.exists(input_file):
        print(f"Error: {input_file} not found. Please run the load test first.")
        return

    with open(input_file, "r", encoding="utf-8") as f:
        data = json.load(f)

    if not data:
        print("Error: JSON data is empty.")
        return

    # Group by mode and scenario
    modes = {}
    for r in data:
        m = r["mode"]
        if m not in modes:
            modes[m] = []
        modes[m].append(r)

    md = [
        "# Load Test Performance Report (V2)",
        "",
        "This report is generated from the raw `load_test_results.json` to ensure consistency between raw data and the presented metrics.",
        "",
        "## Methodology",
        "- **Endpoints Tested**: `/api/v2/journey-plans/search`",
        "- **Concurrency Tiers**: 1, 10, 25, 50 concurrent workers",
        "- **Scenarios**: 0-Transfer, 1-Transfer, 2-Transfer routes",
        "- **Duration**: 2 seconds per tier",
        ""
    ]

    for mode, results in modes.items():
        md.append(f"## {mode} Mode Performance")
        md.append("")
        md.append("| Scenario | Concurrency | RPS | Errors (%) | P50 (ms) | P95 (ms) | P99 (ms) | CPU (%) | Mem (MB) |")
        md.append("|---|---|---|---|---|---|---|---|---|")
        
        for r in results:
            scen = r["scenario"]
            c = r["concurrency"]
            rps = f"{r['rps']:.1f}"
            err = f"{r['errors'] / r['requests'] * 100:.1f}" if r['requests'] > 0 else "0.0"
            
            p50 = f"{r['vectorB']['p50']:.0f}"
            p95 = f"{r['vectorB']['p95']:.0f}"
            p99 = f"{r['vectorB']['p99']:.0f}"
            
            cpu = f"{r.get('cpu', 0):.1f}"
            mem = f"{r.get('memoryMB', 0):.1f}"
            
            md.append(f"| {scen} | {c} | {rps} | {err} | {p50} | {p95} | {p99} | {cpu} | {mem} |")
        
        md.append("")

    with open(output_file, "w", encoding="utf-8") as f:
        f.write("\n".join(md))

    print(f"Successfully generated {output_file}")

if __name__ == "__main__":
    generate_report()
