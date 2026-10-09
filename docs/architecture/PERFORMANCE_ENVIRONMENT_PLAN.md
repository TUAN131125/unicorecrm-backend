# Isolated performance environment readiness

Date: 2026-10-09. This is a preparation plan, not infrastructure admission or capacity certification. It follows the [Search decision](CONTACT_SEARCH_ARCHITECTURE_DECISION.md) and [product readiness review](LEAD_CONTACT_PRODUCT_READINESS.md). No database, load test, key ring or deployment configuration is created by this task.

```text
CONTACT_100K_QUERY_BENCHMARK: MEASURED
API_SATURATION_ROOT_CAUSE: NOT_ESTABLISHED
10K_CONCURRENT_CAPACITY: NOT_ESTABLISHED
ApplicationName: UnicoreCRM.AI
Shared production key-ring topology: NOT_DECIDED
```

## What existing evidence supports

The 100K evidence is persistence-call timing on an isolated synthetic fixture hosted on a shared machine. Earlier single-replica API runs on 10K showed throughput saturation without establishing its cause. The laptop lacked guarded RAM headroom for further stages. Neither observation proves production resource requirements, multi-replica behavior or 10,000 concurrent users.

[ApiLoad.cs](../../scripts/SqlPerformanceBench/ApiLoad.cs) accepts only an owned loopback HTTP API, a known local API process and SQL process, integrated authentication, a GUID-named fixture database and its ownership marker. Its supported client stages are 25/100/250/500/1000, with 2/3/4/6/8 GiB available-memory reserves. Those are stop guards, not machine sizing measurements. It uses a 5-second warmup, a 30-second measured request-start window and 500 ms think time. HTTP body latency excludes client JSON processing and think time; drain requests and failures must retain their own accounting. This short closed-loop run is not a soak test or an open-loop capacity model.

Workloads include mixed, light, rare/no-match search, summary and independent Lead Kanban pages. The observer has a distinct SQL diagnostic pool; its session wait is not proof of application connection-pool waiting. Existing loopback/process restrictions must remain intact. A remote generator or multi-host runner requires a separately approved harness extension with equally strong ownership checks; changing the URL or adding an unsupported 10K stage is insufficient.

## Isolation and initial resource assumptions

The following ranges are planning hypotheses for a first controlled environment. They are not measured minimums, approved production sizing or a vendor selection.

| Role | Initial assumption | Evidence to collect before resizing |
| --- | --- | --- |
| Dedicated SQL host/VM | 8–16 vCPU, 32–64 GiB RAM; initially about 250 GiB provisioned SSD/NVMe capacity including data/log/evidence headroom | Working set, grants, CPU, waits, actual used pages, log growth, latency/IOPS/throughput and storage free space |
| Dedicated API host/VM | 4–8 vCPU, 8–16 GiB RAM per measured replica | Request concurrency, GC allocation/pauses, heap, ThreadPool queue/workers, CPU and pool acquisition |
| Separate load-generator host/VM | 4–8 vCPU, 8–16 GiB RAM initially | Generator CPU/queue, scheduling delay, connections, sockets, network utilization and achieved arrival rate |
| Evidence/observer process | Separately budgeted storage and collection overhead | Lost samples, observer CPU/IO and telemetry volume |

Option 1 is reserved physical machines; option 2 is isolated VMs on a reserved host with documented CPU/memory/storage contention; option 3 is temporary hosted VMs after provider, network, cost and credential approval. Co-located VMs do not establish physical isolation. A laptop with constrained headroom is not an accepted load environment. Do not lower resource guards or change instance-wide SQL settings to make a stage run.

SQL Server edition, version, compatibility level, collation and patch level must be explicit in the run manifest. Edition is NOT_DECIDED: choose an approved test edition whose relevant behavior matches the deployment question, and have the environment owner confirm licensing and feature constraints. Do not extrapolate capacity from an edition with different effective limits. Data/log paths must be SQL Server-managed paths, with sufficient free space and observable file-growth behavior. Separate data/log IO where the selected storage actually supplies independent capacity; merely using different folders proves no isolation.

## Dataset and execution stages

Generate synthetic, deterministic, non-personal records using the [existing fixture harness](../../scripts/SqlPerformanceBench/README.md). Retain seed/run ID, generator version, SHA, row counts, index/migration fingerprint, field/null/JSON shapes, workspace/owner skew, relationships and Tasks distribution. Start with 10K then 100K Contacts and a documented Lead/Task cardinality. Reuse the known common/rare/no-match categories without claiming they represent a commercial workload. Add authorized OWN/WORKSPACE/FLS and Tasks-dependent cohorts after their test contract is specified; the existing single-token workload does not cover them automatically.

| Stage | Question and admission gate |
| --- | --- |
| Safety/correctness preflight | Verify owned resources, migrations, identities, redaction, counts, cursors, commands and cleanup before measuring load. |
| Single-instance persistence benchmark | Establish SQL query/plan/IO behavior separately from HTTP. Reopening Contact search research still requires the ADR conditions; this plan does not authorize another search run. |
| Single-replica HTTP benchmark | Correlate one API replica with SQL and generator telemetry; use supported guarded stages, repeated order and controlled warm/cold policy. Establish the earlier saturation cause before tuning. |
| Multi-instance benchmark | Compare 1/2/more replicas with fixed total and per-replica load, shared SQL, explicit routing and equivalent authentication. More replicas alone are not distributed-runtime proof. |
| Distributed deployment verification | Test cursor/token compatibility, shared protection keys, replay/idempotency, concurrency, failover/restart and dependency behavior using an approved deployment topology. |
| Production capacity verification | Use accepted endpoint SLOs/error budgets, workload mix, think-time/session model, arrival-rate/burst policy and sustained/soak durations. A 10K-user experiment needs an approved isolated generator and harness contract first. |

Do not pool unlike runs or treat virtual users as in-flight requests. Record active sessions, started/completed/drained requests, offered and achieved rate, response bytes, endpoint mix, replica count and resource allocations. Product owners must decide acceptable p95/p99/error thresholds and search tradeoffs before a capacity PASS can be defined.

## Required synchronized observability

- API: endpoint/replica/cohort p50/p95/p99, throughput, HTTP 4xx/5xx/timeouts/cancellations, incomplete/drained requests and request correlation IDs. Attribute the operation actually sent, including cursor fallback.
- Runtime: API CPU, working set, allocation rate, managed heap, GC generations/pauses, ThreadPool queue/workers, request queue and starvation evidence. Separate generator runtime counters from API counters.
- Database: actual plans and plan identity, estimated/actual rows, reads, CPU/elapsed time, grants/spills, SQL process CPU, waits/blocking/deadlocks, data/log IO latency and file growth. Distinguish count, qualification and materialization; capture clock and sampling alignment.
- Connections: application pool configuration, active/idle connections, acquisition timing, connection-open spans, command timing and pool timeout/errors. A reader-acquisition span alone does not prove a pool wait; pair it with pool/SQL/runtime evidence.
- Host/network: CPU allocation/throttling, available RAM, paging, storage capacity/IO and generator/network saturation. Collect at least the same run/window IDs and UTC time boundaries across all processes.

Use documented read-only diagnostic privileges scoped by the environment owner. Do not grant arbitrary elevated SQL access or alter a shared instance. Keep secrets and token values out of logs. Benchmark telemetry overhead must be measured and retained with results.

## DataProtection and replica prerequisites

[ProvidersModule](../../src/UnicoreCRM.AI/Providers/ProvidersModule.cs) sets `UnicoreCRM.AI`; a configured `AI:DataProtection:KeyRingPath` persists filesystem keys, and non-Development startup rejects a missing durable location. Production shared key-ring topology remains NOT_DECIDED. No configuration or key file changes are part of this plan.

Before cross-replica verification, approve key-ring location, access permissions, encryption/backup, rotation/lifetime and restart/failover behavior. Verify a protected cursor created on replica A can be consumed under the same authorized scope on replica B and remains denied under another scope. Do not delete the existing key ring, introduce Redis/shared storage or infer a production topology from Development behavior.

## Safety, stop gates and cleanup

Every future fixture requires a task-owned disposable database, GUID ownership marker, manifest containing exact instance/database/data/log paths and initial file state, and a fresh absolute evidence directory outside Git. Verify SQL Server-managed data/log directories before creation; never create MDF/LDF at a drive root. Use explicit authorized credentials, not an assumed `sa` account. Do not modify user databases, `appsettings.Development.json`, staged files or instance-wide settings.

Stop on insufficient guarded headroom, low disk space, ownership mismatch, unexpected file paths, incorrect/redacted response leakage, command inconsistency, uncontrolled generator saturation or resource exhaustion. Latency/error abort thresholds require agreed SLOs; no silent threshold relaxation is allowed. Retain NOT_RUN_RESOURCE_LIMIT and failed stages honestly.

Cleanup must revalidate manifest/marker, exact resources, SQL-managed files and active sessions. Only the verified task-owned database/processes may be removed through supported database/process operations. No blind DROP, direct MDF/LDF deletion, key-ring deletion or recursive Git cleanup. Record cleanup outcome and remaining resources even after a failed run; preserve evidence outside Git without secrets.

## Decisions required before execution

Environment owner and budget; SQL edition/version; physical/VM/storage/network isolation; representative dataset/workload and endpoint SLOs; generator/remote harness admission; replica/routing topology; shared key-ring decision; diagnostic permissions; retention and cleanup responsibility. Operational cost includes reserved compute time, storage/telemetry retention, SQL entitlement where applicable and operator time. No provider, price estimate or production design is approved by these planning ranges.

No load test runs in this closure. API saturation root cause and 10K concurrent capacity remain NOT_ESTABLISHED.
