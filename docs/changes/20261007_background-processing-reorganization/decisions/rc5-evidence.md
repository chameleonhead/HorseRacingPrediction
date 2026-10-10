# RC5 local performance evidence

## Scope and provenance

This is local action-level evidence, not a Lightsail CPU measurement or a full hosted-loop benchmark. Lead accepts RC5 after the bounded attribution controls below; no claim that every action allocates less, or that live CPU is reduced, follows.

- Baseline: `0decdedfad1d3a0dedb07a3f8b6cabf5632a3ca7`; current: `9d94f1ff0f1b664d15a3f3566cac438f616c7163` plus the attributed reorganization patch, including TEMP worklist B.
- Windows 10.0.26200 X64, .NET 10.0.11, SQLite 3.53.3, workstation GC, Release builds; same machine and identical fixture/harness. Fresh process per workload, one warmup, 100 dispatcher and 10 producer/recovery samples. Setup/restoration is outside timing; no competing builds/hosts during measurements.
- Native SQL trace enabled, profile disabled. Current dispatcher/producer includes the in-memory runtime recorder; baseline lacks it. Recovery is the direct Store adapter in both. SQL categories separate operation from diagnostic telemetry.
- Raw TRXs retained outside both checkouts at `C:/Users/yuto.nagano/AppData/Local/HorseRacingPrediction/verification/20261008-background-reorganization/{baseline,current}/RC5-final-{baseline,current}-<scenario>.trx`. Repeat files use `RC5-repeat-<version>-AllLanesReady.trx`. Lead independently parsed all fourteen final results and their fixture manifests. Each run passed its semantic assertions; a passing harness alone is not RC5 acceptance.
- Original zero-send held/history fixtures were invalid comparisons and excluded. Earlier trace-off, scalar-query, A and B profiling diagnostics remain historical evidence, not substitutes for the final comparisons.

Harness SHA-256:

- MeasurementTests: `4EAFC1B61160737C6CB15C5C3C509E5D49690B7A56367212A4436015462F9FFF`
- Instrumentation: `79C8EB9954653E4134F396ADFB830C590913752B6EDAD6D91D5F109C11476DC2`

Measured DLL SHA-256 (current / baseline):

| Assembly | Current | Baseline |
| --- | --- | --- |
| API | E8A75729EE90C193176CBB19FAF0463BDFEF38F7FD23ADC5EB4C790CF3AC1215 | DC3A4F2333F6760FC642C27F6977F01CA59544C9860B4EF3F3FF0A9D261A4DE5 |
| CollectionOperations | 938531BA81E8030AA3CDB90DF1EE78F9F11333E85BFB88D2F7809D05A8E49777 | DE746DC8321DEAC424623A216ADF319C439942FC3ADB451C451B40976E7857CE |
| API.Tests | D5C5F74D809A3C62CE164A1A1670D482DA1FBF9F4B051778A8DDA62F49E54A77 | 5A6F92476D86857FB44584C1CD54B64F07338D4FCC65348A65E4156DF4E12AFB |

## Final workload results

Logical-business fixture SHA-256 matched independently in both raw results for every workload:

| Workload | SHA-256 |
| --- | --- |
| Empty | E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855 |
| CapacityFull | F4FCB22BF05F7D8D7F9C737F9D8A87DC1AC42FE06E9381B2EDFF0213E0CCD85B |
| AllLanesReady | 33E5D264568F5FEB690BA6C71E76ADB2FE5D91D7EEB9BD3F07F6C9A19B7D30EB |
| DenseActivePrefix | 24A4835D532409A4C329A3D5D5F5B7486C37877A7BB7853B4694B71F6A7C334E |
| HeldPrefix | 5B58DB605865372BC8E11D0DC2DB646D9B307D095596C740AFFBAC5704E2BE8F |
| FixedLiveHistory1x | B5E1518BAB2E889C7DEA290B54D3977362935384D08C24E4F95BAB802134BD66 |
| FixedLiveHistory10x | E4BA7B9ED4E533553C547FAA0AFC61206DA36DBD1CB399E41A0448959BC91BC0 |

Each cell is baseline → current. Latencies are milliseconds, allocations are bytes per action (median), SQL/rows are operation-only totals across measured cycles. Dispatcher telemetry independently uses 9 statements including 7 SELECTs in each workload/version and is excluded below.

| Dispatcher workload | p50 | p95 | Allocated bytes p50 | Sends | SQL | Returned rows |
| --- | --- | --- | --- | --- | --- | --- |
| Empty | 44.08 → 37.19 | 52.08 → 46.62 | 470680 → 439824 | 0 → 0 | 500 → 1000 | 21800 → 30800 |
| CapacityFull | 50.36 → 33.10 | 59.39 → 45.33 | 909712 → 133896 | 0 → 0 | 1300 → 200 | 29300 → 15400 |
| AllLanesReady | 86.39 → 77.65 | 97.07 → 90.91 | 1980544 → 2039896 | 100 → 100 | 3200 → 3600 | 47400 → 60700 |
| DenseActivePrefix | 86.12 → 79.72 | 97.39 → 95.33 | 2338736 → 2369696 | 100 → 100 | 4200 → 4600 | 50200 → 62500 |
| HeldPrefix | 206.35 → 200.39 | 243.10 → 235.12 | 36788008 → 13981824 | 100 → 100 | 3200 → 8900 | 262800 → 266500 |
| FixedLiveHistory1x | 95.79 → 85.74 | 110.60 → 99.44 | 3322800 → 3726776 | 100 → 100 | 4500 → 8100 | 64400 → 73300 |
| FixedLiveHistory10x | 101.53 → 87.40 | 119.44 → 99.49 | 6898328 → 3723864 | 100 → 100 | 4500 → 8100 | 122000 → 73300 |

| Schedule-producer workload | p50 | p95 | Allocated bytes p50 | Tasks created | SQL | Returned rows |
| --- | --- | --- | --- | --- | --- | --- |
| Empty | 33.98 → 29.46 | 37.57 → 33.90 | 182184 → 103152 | 0 → 0 | 50 → 10 | 1450 → 760 |
| CapacityFull | 386.60 → 406.57 | 451.64 → 472.84 | 5483936 → 6561408 | 200 → 200 | 3450 → 4210 | 17250 → 17960 |
| AllLanesReady | 378.19 → 468.85 | 448.44 → 558.81 | 5486016 → 6558040 | 200 → 200 | 3450 → 4210 | 17250 → 17960 |
| DenseActivePrefix | 34.21 → 598.00 | 43.37 → 661.61 | 1284192 → 10435016 | 0 → 320 | 50 → 6730 | 6450 → 28280 |
| HeldPrefix | 7710.99 → 1499.34 | 7866.17 → 1595.38 | 100703720 → 59336416 | 0 → 0 | 70050 → 30010 | 401450 → 405760 |
| FixedLiveHistory1x | 437.88 → 442.73 | 466.17 → 525.20 | 6558656 → 7885552 | 240 → 240 | 4130 → 5050 | 20650 → 21880 |
| FixedLiveHistory10x | 437.36 → 443.50 | 529.14 → 520.56 | 6559056 → 7895480 | 240 → 240 | 4130 → 5050 | 20650 → 21880 |

DenseActivePrefix is intentionally not equal work: the old candidate prefix starves later work, whereas the new SQL prefilter reaches 320 tasks. Higher elapsed time there is not a like-for-like regression.

| Recovery workload | p50 | p95 | Allocated bytes p50 | Tasks created | SQL | Returned rows |
| --- | --- | --- | --- | --- | --- | --- |
| Empty | 27.91 → 29.45 | 29.24 → 33.08 | 64152 → 144320 | 0 → 0 | 10 → 20 | 720 → 1530 |
| CapacityFull | 200.34 → 79.08 | 220.63 → 97.78 | 3380512 → 2772448 | 70 → 70 | 1710 → 1730 | 13300 → 4250 |
| AllLanesReady | 197.61 → 84.04 | 215.00 → 87.17 | 3376816 → 2755208 | 70 → 70 | 1710 → 1730 | 13300 → 4250 |
| DenseActivePrefix | 203.65 → 79.31 | 209.89 → 86.50 | 3381248 → 2771440 | 70 → 70 | 1710 → 1730 | 13300 → 4250 |
| HeldPrefix | 207.21 → 77.52 | 222.44 → 174.39 | 3392072 → 2771200 | 70 → 70 | 1710 → 1730 | 13370 → 4320 |
| FixedLiveHistory1x | 201.54 → 76.79 | 210.15 → 90.13 | 3381936 → 2773512 | 70 → 70 | 1710 → 1730 | 13370 → 4320 |
| FixedLiveHistory10x | 196.13 → 81.22 | 305.33 → 90.00 | 3392720 → 2778528 | 70 → 70 | 1710 → 1730 | 13370 → 4320 |

## Safety and scaling evidence

Lead independently reconciled the operation-only writer and materialization counters in the original raw final results:

| Workload/action | Immediate writer transactions baseline → current | Write statements baseline → current | Current entity materializations / tracked entities |
| --- | --- | --- | --- |
| Empty dispatcher,100 cycles | 100 → 0 | 0 → 0 | 100 / 0 |
| Empty schedule,10 cycles | 10 → 0 | 0 → 0 | 0 / 0 |
| Empty recovery,10 cycles | 0 → 0 | 0 → 0 | 0 / 0 |
| CapacityFull dispatcher,100 cycles | 200 → 0 | 0 → 0 | 0 / 0 |
| CapacityFull schedule,10 cycles | 210 → 200 | 1000 → 1000 | 600 / 1400 |
| CapacityFull recovery,10 cycles | 70 → 20 | 410 → 460 | 90 / 500 |
| History1x and10x dispatcher,100 cycles each | 200 → 100 | 2200 → 3500 | 19800 / 8500 in both current workloads |

Full-capacity dispatcher resolver inputs are0, with no candidate entity materialization or writer transaction. Historical cardinality×10 does not increase current dispatcher entity materialization, tracked entities, resolver inputs or returned operation rows. Added TEMP statements are counted as writes but do not imply writes to business history; normal task/output counts and preservation proofs are separate evidence. Full-capacity means dispatch capacity is exhausted; finite schedule/recovery producers still create durable work, so their nonzero writes are expected.

Original peak working-set observations (MiB, process peak through the action, not isolated per-action allocation): Empty dispatcher153.28→154.57, schedule161.45→158.79, recovery162.55→163.39; CapacityFull150.89 current dispatcher versus161.39 baseline. History1x dispatcher171.85→178.99, history10x177.87→184.83; later recovery peaks185.75→190.93 and186.71→192.27. All are below a10% increase. Oldest seeded due wait remains30s in comparable nonempty fixtures; this is fixture age, not a measured hosted-service SLA. Raw lock-wait counters are0 in these uncontended workload actions; actual contention is proven separately by the raw SQLite writer-lock tests, and no production wait claim follows.

HeldPrefix dispatch preserves 100 sends while resolver inputs fall227736→106936 and entity materializations283700→109900. Fixed-live history1x/10x current resolver inputs remain11124/11124 and materializations19800/19800; baseline inputs26824/142024 and materializations40000/155200 scale with history. TEMP B replaces repeated expensive live-source page evaluation with one exact provider-bound key seed and indexed pages≤128. EXPLAIN confirms `SEARCH temp.HeldResourceWorklist USING INTEGER PRIMARY KEY (rowid>?)`. Extra inexpensive TEMP statements mean this is not a blanket SQL/row-count reduction.

Collector183/API52 plus actual Store partial-create/cancel/pool lifecycle2 pass. Fresh whole-envelope/identity checks, full-capacity short-circuits, unknown fail-closed behavior, cleanup/discard and schema/data preservation are not relaxed for performance.

## Historical diagnostic disposition (closed by controls below)

Opposite-order AllLanesReady repeat: schedule p50 375.4782→377.1598ms, p95 386.4610→459.9778ms, allocated p50 5492864→6554520 bytes, same200 tasks. Median regression did not reproduce; tail (+19%) and allocations (+19.3%) did. Resolver inputs200→400 and operation SQL3450→4210 confirm added bounded checks, but do not quantitatively explain their time/allocation. Current recorder presence is another potential contribution. Ten-cycle summaries omit individual samples; do not waive the gate as noise.

History1x dispatcher allocations increase403976 bytes (+12.2%) despite improved latency and reduced materialization. Empty recovery adds80168 bytes, one read per cycle and about1.54ms median with zero writes/tasks. Those bounded read/safety costs remain visible, not an unqualified improvement claim. Next diagnostics preserve semantics and default protocol, isolate recorder/observer contributions, retain raw cycle samples and increase diagnostic sample size before interpreting tails. No production safety check may be removed merely to make a benchmark pass.

## Stage1 allocation controls (2026-10-10)

Five fresh-process runs passed1/1 each; Lead independently parsed all results and all100 samples/action. Same fixture hash33E5D264…B7D30EB,100 dispatch/producer/recovery cycles. Every cycle sends1 envelope, schedules20 tasks and recovers7 tasks. Files are I4a-stage1-{default,recorder-off,trace-off}-{baseline,current}.trx under the durable baseline/current roots; only current has the recorder-off control. Harness source ACE68… and instrumentation79C8… are preserved in linux/source-provenance/final-harness. These are diagnostic repeats; absolute timings differ from the Oct8 run, so compare paired modes rather than combining their latencies.

| Mode/action | p50 ms | p95 ms | Allocated bytes p50 | Process peak MiB |
| --- | --- | --- | --- | --- |
| Baseline default/dispatcher | 199.21 | 249.28 | 1972528 | 175.07 |
| Baseline default/schedule-producer | 606.52 | 673.69 | 5491072 | 189.21 |
| Baseline default/backfill-recovery | 363.62 | 452.51 | 3391824 | 202.02 |
| Current default/dispatcher | 179.10 | 229.24 | 2039840 | 179.07 |
| Current default/schedule-producer | 596.87 | 674.70 | 6581848 | 190.00 |
| Current default/backfill-recovery | 162.32 | 201.15 | 2769232 | 204.50 |
| Current recorder-off/dispatcher | 160.81 | 187.27 | 2037936 | 176.47 |
| Current recorder-off/schedule-producer | 617.50 | 720.59 | 6567720 | 184.75 |
| Current recorder-off/backfill-recovery | 141.58 | 178.95 | 2766088 | 203.37 |
| Baseline trace-off/dispatcher | 191.70 | 225.79 | 1827496 | 170.00 |
| Baseline trace-off/schedule-producer | 595.45 | 695.94 | 4739112 | 175.73 |
| Baseline trace-off/backfill-recovery | 391.27 | 468.30 | 2968528 | 188.10 |
| Current trace-off/dispatcher | 150.70 | 178.63 | 1742744 | 171.29 |
| Current trace-off/schedule-producer | 603.11 | 684.45 | 5585576 | 178.86 |
| Current trace-off/backfill-recovery | 139.00 | 167.30 | 2339864 | 193.00 |

Default normal producer tail differs+0.15%, trace-off tail−1.65%; the earlier ten-sample tail increase is not reproduced. Allocation increase persists: default+19.86%, trace-off+17.86%. Recorder-off changes normal allocation by only14128B versus default; timing changes in mixed directions, so no recorder-only causal timing claim is justified. All trace-off SQL/row counters are explicitly unavailable, while semantic checks remain active.

The exact normal operation-statement delta is +4 SELECT/candidate (fresh joined state/resource eligibility, active-task EXISTS, additional hold resource context and active hold IDs), minus4 legacy cleanup/count statements/cycle. Across100 cycles this is8000−400=7600, matching34500→42100 statements. Resolver inputs increase2000→4000; task writes/counts are unchanged. The controls establish that SQL observation and recorder are not the sole source of normal allocation growth; they do not isolate every allocated byte. A same-binary guarded/direct diagnostic and History1x/Empty trace-off pairs remain the bounded final attribution work.

## Final bounded attribution and acceptance (2026-10-10)

Lead independently parsed all six `I4a-attribution-*.trx` files in the durable current/baseline roots: each Passed1/1, matching paired logical hashes and per-cycle semantic assertions. Harness E1A9F61629C8770AF15ED9EB569C82ED68EB4DB9459468724124BEF96984F01F is identical in both checkouts; instrumentation79C8… and production DLLs are unchanged. The direct-request path is test-only, explicit opt-in, current/AllLanesReady only, trace/recorder off, and labelled diagnostic. Preflight proves exactly20 eligible unheld fixture resources outside timing. Every measured receipt creates a task. It retains RequestCore's safety checks but bypasses the scheduler's extra eligibility/first identity stage; it is not a proposed production algorithm.

| Trace-off comparison | p50/p95 ms | Allocated bytes p50/p95 | Equal output |
| --- | --- | --- | --- |
| Current AllLanesReady guarded → direct diagnostic,100 producer cycles | 609.51/691.20 → 582.57/671.22 | 5587528/5596032 → 4566480/4574808 | 2000 tasks; resolver4000→2000 |
| History1x dispatcher baseline → current,100 cycles | 182.62/232.07 → 173.14/211.05 | 2995656/3053936 → 3118240/3203480 | 100 sends; resolver26824→11124 |
| History1x producer baseline → current,10 cycles | 1531.07/2528.60 → 772.02/843.34 | 5776264/5912160 → 6699208/6705784 | 240 tasks; resolver240→480 |
| History1x recovery baseline → current,10 cycles | 388.95/447.15 → 153.03/180.29 | 2969944/2985760 → 2336856/2362168 | 70 tasks |
| Empty dispatcher baseline → current,100 cycles | 94.90/118.36 → 74.44/108.26 | 462848/472808 → 378552/389472 | 0 sends |
| Empty producer baseline → current,10 cycles | 63.37/88.76 → 48.99/63.63 | 170784/172480 → 101152/104168 | 0 tasks |
| Empty recovery baseline → current,10 cycles | 49.25/58.36 → 59.70/68.54 | 65776/71056 → 139448/140192 | 0 tasks |

The guarded/direct same-binary contrast isolates1,021,048B/cycle of the additional producer path (roughly51KB per eligible candidate), exceeding the observed baseline/current normal allocation delta. Exact allocation of individual guards is not established: paging/context/query construction and diagnostic receipt assertions are included. The new safety work is retained. History dispatcher trace-off residual is+4.1%, not the trace-on+12.2%; observation explains much of that measured gap, with p95−9.1%.

Empty recovery's extra73,672B and10.45ms median persist without tracing. Source review and operation counters identify a fixed no-work cost: legacy-existence hint followed by bounded typed selection, two context/query lifetimes instead of one. No business row materialization, writer transaction, task or write occurs. This is an explained additive recovery-metadata check, not noise, and is explicitly retained to preserve classification and bounded recovery. No per-byte attribution claim is made. Empty current process peak is165,666,816B versus169,508,864B baseline; paired history peaks also rise less than10%. These are process peaks, not retained managed heap measurements.

Acceptance: mandatory empty/full writer and history-cardinality gates pass; semantic output is equal where comparable, dense-active work newly progresses, held work is bounded, nonempty recovery improves, and normal producer tail regression does not reproduce in100-cycle paired repeats. Investigated allocation increases are due to added safety/recovery work or observation, not unexplained whole-history work. No safety check was removed to meet performance gates. No further benchmark matrix, production operation, CPU guarantee or hosted SLA is inferred. Reconsider after separately authorized live observation if empty fixed costs, normal producer allocations or due-job wait dominate under actual load.
