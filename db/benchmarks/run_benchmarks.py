"""SQL benchmark runner (spec §11.4): 1 warm-up + 5 measured executions per query, medians reported.

    python db/benchmarks/run_benchmarks.py baseline        # DAY-4, primary key only
    python db/benchmarks/run_benchmarks.py after-index     # DAY-6, after each change

For every query it records logical reads on dw.PriceSnapshot and in total (SET STATISTICS IO), CPU and elapsed time of the
whole statement (SET STATISTICS TIME), and saves the actual execution plan (SET STATISTICS XML) as a .sqlplan file that
SSMS / Azure Data Studio can open. Runs inside the priceradar-sql container through sqlcmd; nothing to install.
Warm cache only: cold-cache numbers need DBCC DROPCLEANBUFFERS and are taken by hand (spec §11.4).
"""
from __future__ import annotations

import json
import re
import statistics
import subprocess
import sys
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
QUERIES = Path(__file__).with_name("queries.json")
OUT = ROOT / "docs" / "benchmarks"
CONTAINER = "priceradar-sql"
RUNS = 5
NS = {"p": "http://schemas.microsoft.com/sqlserver/2004/07/showplan"}


def sqlcmd(sql: str) -> str:
    # -I: QUOTED_IDENTIFIER ON (filtered indexes); -y 0: never truncate the showplan XML column.
    cmd = ["docker", "exec", "-i", CONTAINER, "bash", "-c",
           '/opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PriceRadar -y 0 -i /dev/stdin']
    result = subprocess.run(cmd, input=sql.encode("utf-8"), capture_output=True, check=True)
    return result.stdout.decode("utf-8", errors="replace")


def measure(sql: str) -> dict:
    out = sqlcmd(f"SET NOCOUNT ON; SET STATISTICS IO ON; SET STATISTICS TIME ON;\n{sql}\nSET STATISTICS TIME OFF; SET STATISTICS IO OFF;")
    reads = [(m.group(1), int(m.group(2))) for m in re.finditer(r"Table '([^']+)'\. Scan count \d+, logical reads (\d+)", out)]
    times = re.findall(r"SQL Server Execution Times:\s+CPU time = (\d+) ms,\s+elapsed time = (\d+) ms", out)
    cpu, elapsed = (int(times[-1][0]), int(times[-1][1])) if times else (0, 0)   # last block = the whole statement
    return {
        "snapshotReads": sum(n for t, n in reads if t == "PriceSnapshot"),
        "totalReads": sum(n for _, n in reads),
        "cpuMs": cpu,
        "elapsedMs": elapsed,
    }


def plan(sql: str, path: Path) -> str:
    out = sqlcmd(f"SET NOCOUNT ON; SET STATISTICS XML ON;\n{sql}\nSET STATISTICS XML OFF;")
    docs = re.findall(r"<ShowPlanXML.*?</ShowPlanXML>", out, flags=re.S)
    if not docs:
        return "no plan captured"
    chosen = next((d for d in docs if "[PriceSnapshot]" in d), docs[0])
    path.write_text(chosen, encoding="utf-8")
    return shape(chosen)


def shape(xml: str) -> str:
    """Physical operators that touch PriceSnapshot / Book, plus warnings, in plan order."""
    root = ET.fromstring(xml)
    ops = []
    for relop in root.iter(f"{{{NS['p']}}}RelOp"):
        obj = relop.find("./*/p:Object", NS)
        if obj is not None and obj.get("Table") in ("[PriceSnapshot]", "[Book]", "[Listing]"):
            index = (obj.get("Index") or "").strip("[]")
            ops.append(f"{relop.get('PhysicalOp')} {obj.get('Table').strip('[]')}.{index}")
    warnings = sorted({w.tag.split('}')[1] for w in root.iter() if w.tag.endswith(("SpillToTempDb", "MemoryGrantWarning", "PlanAffectingConvert", "NoJoinPredicate"))})
    text = " → ".join(dict.fromkeys(ops)) or "constant/other"
    return text + (f" (warnings: {', '.join(warnings)})" if warnings else "")


def main() -> None:
    label = sys.argv[1] if len(sys.argv) > 1 else "baseline"
    queries = json.loads(QUERIES.read_text(encoding="utf-8"))["queries"]
    (OUT / "plans").mkdir(parents=True, exist_ok=True)
    (OUT / "results").mkdir(parents=True, exist_ok=True)

    results = []
    for q in queries:
        measure(q["sql"])   # warm-up: compile the plan and load pages into the buffer pool
        samples = [measure(q["sql"]) for _ in range(RUNS)]
        med = {k: statistics.median(s[k] for s in samples) for k in samples[0]}
        plan_file = OUT / "plans" / f"{q['id'].lower()}-{label}.sqlplan"
        results.append({**q, "label": label, "median": med, "samples": samples,
                        "plan": plan(q["sql"], plan_file), "planFile": str(plan_file.relative_to(ROOT)).replace("\\", "/")})
        print(f"{q['id']} {label:<12} reads(PriceSnapshot)={med['snapshotReads']:>6.0f}  total={med['totalReads']:>6.0f}  "
              f"cpu={med['cpuMs']:>4.0f} ms  elapsed={med['elapsedMs']:>4.0f} ms  | {results[-1]['plan']}")

    version = sqlcmd("SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('ProductVersion') AS varchar(20));").split()[-1]
    out = {"label": label, "takenAtUtc": datetime.now(timezone.utc).isoformat(timespec="seconds"), "sqlServer": version,
           "runsPerQuery": RUNS, "results": results}
    target = OUT / "results" / f"{label}.json"
    target.write_text(json.dumps(out, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {target.relative_to(ROOT)}")


if __name__ == "__main__":
    main()
