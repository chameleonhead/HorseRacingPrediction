# API endpoint 構成の機能別分割

- Status: Implemented
- Change record schema: 2
- Owner: Main agent / user
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | 77 routeを77 operation endpoint filesへ移動し、共通compositionとfeature-local helpersへ分割した |
| Verification | Complete | solution build、API tests 320 passed/1 skipped、format、IDE0005、diff、CodeGraph、route/metadata inventoryが成功 |
| Deployment/operation | Not applicable | 内部構成の変更であり、route と API contract は維持する |

## Context

`src/HorseRacingPrediction.Api/EndpointExtensions.cs` は 2,661 行、約 156 KB あり、health と主要ドメインの 58 endpoint が `MapApiEndpoints` に集中している。Horse、Jockey、Trainer、Race、Prediction、Owner、Memo、Machine Learning の mapping、handler、query mapping helper が同居し、変更箇所の発見とレビューが難しい。

一方、API project は .NET 10 の Minimal API として構成されている。既に `EndpointExtensions.*.cs` の partial files と `CollectionController/*EndpointExtensions.cs` があり、機能単位の mapper へ分割する前例がある。Controller、`AddControllers`、`MapControllers` は現在使われていない。

## Goals

- `EndpointExtensions.cs` を、最近の ASP.NET Core Minimal API の構成に沿った feature/vertical-slice 単位のフォルダと mapper に分割する。
- `MapApiEndpoints()` を薄い composition facade として残し、production と test host の入口を安定させる。
- route、HTTP method、route name、tag、request/response contract、status code、filter の適用範囲と順序を変えない。
- endpoint 固有の helper は各 feature に置き、複数 feature で使う paging/search helper だけを小さな shared utility に分離する。

## Non-goals

- API の route、payload、認証方式、業務動作の変更。
- Contracts project にある公開 request/response type の移動。
- domain/application architecture の再設計。
- MVC を選ばない場合の Controller 導入。
- handler logic を application service へ全面移動する大規模な layered-architecture 改修。

## Documentation updates

- `README.md`: 「全 endpoint は単一 EndpointExtensions.cs を参照」という記述を削除し、`MapApiEndpoints()` facade、operation 単位 feature folders、共通 composition の配置を canonical layout として記載した。
- `docs/10-domain-design.md`: 現行 runtime と矛盾する古い `Controllers/` project sketch を、Minimal API の endpoint 単位 feature folders と composition extension に更新した。
- `docs/00-system-architecture.md`: `/api` と API key の外部境界は現行記述のままで正しく、更新不要と判断した。

## Technical impact

### Current runtime invariants

- `MapApiEndpoints()` の production caller は `Program.cs`、test caller は `TestApplicationFactory.cs`。
- write endpoints は一つの `/api` route group を共有し、次の順で filter を適用する。
  1. `ApiKeyEndpointFilter`
  2. `RaceWriteEndpointFilter`
  3. `RaceActiveCollectionEndpointFilter`
- read endpoints は原則 filter なし。ただし race context、horse/jockey history、ML prediction の一部だけ `RacePredictionReadEndpointFilter` を使う。
- `POST /api/ml/train` は現状 write group 外である。今回の構成変更では意図を推測して認証動作を変更せず、そのまま維持する。
- route ごとの `WithName`、`WithTags`、`Produces` metadata を維持する。

### Approved-direction Minimal API layout

```text
HorseRacingPrediction.Api/
  Endpoints/
    Health/
      GetHealthEndpoint.cs
    Horses/
      RegisterHorseEndpoint.cs
      GetHorseEndpoint.cs
      SearchHorsesEndpoint.cs
      HorseEndpointMappings.cs      # feature 内で複数 endpoint が共有する変換のみ
    Jockeys/
      RegisterJockeyEndpoint.cs
      GetJockeyEndpoint.cs
      SearchJockeysEndpoint.cs
    Trainers/
      RegisterTrainerEndpoint.cs
      GetTrainerEndpoint.cs
      SearchTrainersEndpoint.cs
    Races/
      GetRaceEndpoint.cs
      SearchRacesEndpoint.cs
      RaceEndpointMappings.cs
      DeclareRaceResultBulkEndpoint.cs
      RaceIdentityValidation.cs
      RaceRefresh.cs
    Predictions/
      GetPredictionEndpoint.cs
      SearchPredictionsEndpoint.cs
    Owners/
      GetOwnerEndpoint.cs
      SearchOwnersEndpoint.cs
      PreviewOwnerIdentityRecoveryEndpoint.cs
      ExecuteOwnerIdentityRecoveryEndpoint.cs
    Memos/
      CreateMemoEndpoint.cs
      GetMemosBySubjectEndpoint.cs
    MachineLearning/
      GetRaceMlPredictionEndpoint.cs
      TrainModelEndpoint.cs
    Identity/
      ResolveHorseIdentityEndpoint.cs
      ResolveRaceIdentityEndpoint.cs
    Repairs/
      GetHorseIdentityRepairEndpoint.cs
      ApplyHorseIdentityRepairEndpoint.cs
      GetSubjectIdentificationRepairEndpoint.cs
      ExecuteSubjectIdentificationRepairEndpoint.cs
    Shared/
      EndpointQueryUtilities.cs
  Extensions/
    EndpointExtensions.cs           # MapApiEndpoints と共通 route groups の composition
```

上記は代表例であり、既存 route はすべて「1 route handler = 1 `*Endpoint.cs`」へ移す。各 endpoint class は route mapping、handler、当該 route だけが使う private helper を所有する。複数 route が使う処理だけを同じ feature の `*Mappings.cs` / `*Service.cs`、複数 feature が使う純粋な paging/search 処理だけを `Endpoints/Shared` に置く。

`Extensions/EndpointExtensions.cs` は business handler を持たず、read 用 `/api` group、既存3 filterを順序どおり持つ write 用 `/api` group、health endpoint、および全 endpoint の `Map` 呼び出しだけを所有する。production と test host は引き続き `MapApiEndpoints()` だけを呼ぶ。

## Decisions

### D1 API programming model

ユーザー判断により **Minimal API の endpoint 単位 feature-folder 分割**を採用する。Controller-based API と hybrid は今回の scope から除外する。

各 endpoint は feature namespace 配下の `internal static class <Verb><Resource>Endpoint` とし、`Map` method と private handler method を持つ。命名は既存の operation name と route の意味を優先し、同名になる場合は `ById`、`BySubject` など識別条件を付ける。公開 API contract type は既存 Contracts / ApiClient project に残す。

### D2 Migration style

Minimal API を選ぶ場合は、一度に handler logic を application service へ再設計せず、まず route composition と feature ownership を分離する。feature 間で使う stateful helper の service 化は、依存関係と test boundary を確認して必要なものだけ行う。

### D3 Endpoint registration contract

- read endpoint は filter なしの `/api` group、write endpoint は既存3 filter付き `/api` groupを受け取り、route pattern は `/api` を除く相対pathで登録する。
- health は API group 外の `/health` を維持する。
- endpoint class が別の endpoint class を呼び出さない。共有業務処理が必要な場合は feature-local helper/service を介す。
- `MapApiEndpoints()` の signature と caller は変更しない。

## Hypothesis ledger

| ID | Claim | Fact / inference | Evidence and falsification | Result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | Minimal API のまま feature 単位に分割できる | Fact | `MapApiEndpoints` の caller、既存 partial files、独立 `*EndpointExtensions`、route integration tests を CodeGraph と repository search で確認 | facade と route group を維持すれば可能 | Design premise |
| H2 | MVC 化は filter semantics を変える危険がある | Fact + inference | write group に Endpoint Filter が3つあり Controller infrastructure は未導入。MVC filters/policies への移植が必要 | 単純な move ではない | D1 の比較材料 |
| H3 | feature-folder が既存 tests を最大限再利用できる | Inference | tests は TestServer 経由で route behavior を検証し、test host も `MapApiEndpoints()` を呼ぶ | facade を維持する案で falsify 可能 | AC2/AC3 で検証 |

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | MVC / Minimal API の選択は migration scope を大きく変える | MVC は filter、binding、metadata の再設計を伴う | Minimal API の endpoint 単位 feature folders を採用 | AC1-AC4 / T1 | Minimal API を推奨 | Minimal APIを選択、Controller-basedは見送り | Resolved in design |
| C2 | write group を feature ごとに再作成すると filter の漏れ・順序変更が起こり得る | auth と race mutation protection の regression | composition で一度だけ作り各 endpoint に渡す | AC2 / T2,T4 | 必須 invariant | 設計全体の承認時に確定 | Resolved in design |
| C3 | `/api/ml/train` は POST だが write group 外 | 整理のついでに挙動を変えると scope creep | 現状を維持し、認証変更は別 change record とする | AC2 / T2,T4 | 現状維持 | 設計全体の承認時に確定 | Resolved in design |
| C4 | helper を全面 service 化すると architecture refactor に拡大する | review と regression scope が急増する | endpoint 分割に必要な最小抽出に限定する | AC1,AC4 / T2 | 段階的変更を推奨 | 設計全体の承認時に確定 | Resolved in design |
| C5 | `/api/ml/train` の直接的な API-host test が見当たらない | filter 適用範囲の accidental change を見逃す | 現状 metadata/auth behavior を固定する regression test を追加する | AC2 / T3 | test 追加を推奨 | 設計全体の承認時に確定 | Resolved in design |

material concern の open decision は解消した。change record 全体の明示承認までは実装へ進まない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | `EndpointExtensions.cs` の巨大な inline mapping がなくなり、既存各 route は domain/feature folder の独立した `*Endpoint.cs` に一対一で属する | T2 | 77 route / 77 endpoint files / 77 facade registrations | Verified |
| AC2 | 全 route の method、pattern、name、tag、status metadata と filter 適用範囲・順序が変更前後で一致する | T2,T4 | route 77/77、name 66/66、tag 66/66、Produces 116/116、filter inventory一致、HTTP tests | Verified |
| AC3 | API project と既存 API test suite が成功し、production と test host は同じ `MapApiEndpoints()` facade を使う | T2,T4 | solution build成功、API tests 320 passed/1 skipped、CodeGraphで2 caller確認 | Verified |
| AC4 | endpoint 固有 helper は feature 内にあり、shared helper は重複せず小さな internal utility に限定される | T2,T4 | feature helper review、endpoint cross-call 0、IDE0005 cleanup成功 | Verified |
| AC5 | README と domain design の project sketch が実装後の canonical layout を正しく説明する | T5 | documentation diff review | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | endpoint単位 Minimal API の設計を承認可能にする | Main agent | Lead tier — architecture decision | - | change recordとcanonical docs | concern/design review | 2026-09-27 user approval | Verified | Lead — architecture/public contract and approval gate | none | usage unavailable; retries 0; corrections 0; reviews 1 |
| T2 | facade、全routeのendpoint単位分割、helper整理、回帰テスト補強を実施する | gpt-6-luna coding worker | Worker tier — user指定、frozen contractに従う機械的抽出 | T1 | `src/HorseRacingPrediction.Api/Endpoints/`, `src/HorseRacingPrediction.Api/Extensions/`, legacy `EndpointExtensions*.cs`, `tests/HorseRacingPrediction.Api.Tests/` | focused tests、API test suite、build、format | [T2-A1](agent-audits/T2-A1.json) と attributable diff | Verified | Worker — bounded refactor under frozen route/filter contract | T2-A1 | usage unavailable; retries 1; corrections 1; reviews 2 |
| T4 | integrated route/metadata/filter equivalence と architecture を review する | Main agent | Lead tier — integration/final acceptance | T2 | read-only review; correctionsはworkerへfocused retry、最終統合のみMain | CodeGraph sync/explore、route inventory、test suite | AC1-AC4 comparison and independent rerun | Verified | Lead — integration/final acceptance cannot be delegated | none | usage unavailable; retries 0; corrections 1; reviews 2 |
| T5 | canonical docs と change record を実装結果へ同期する | Main agent | Lead tier — canonical design ownership | T4 | README, domain design, change record | docs diff and change-record validator | canonical docs and this final record | Verified | Lead — canonical record and completion reconciliation | none | usage unavailable; retries 0; corrections 0; reviews 1 |

User指定により実装は一人のgpt-6-luna workerへ委譲し、production/testのwrite ownerを統一する。route、contract、filter semantics、`/api/ml/train`の現状挙動、公開type配置はfrozen decisionであり、変更が必要ならworkerは実装せずMainへ戻す。MainはAC group単位で独立レビューし、失敗時は一回のfocused correction後にscopeまたはtierを再評価する。

## Review gates

- **Design and task-split review (2026-09-27, Main):** CodeGraph inventory and two independent read-only investigations agree that endpoint単位 feature-folder extraction is separable after the shared composition contract is frozen. D1-D3 are settled; implementation tasks remain Proposed until explicit approval.
- **Concern and agreement review (2026-09-27, Main):** authentication/filter order, ML train exception, helper scope, route metadata, test-host parity, documentation drift, routing/review cost were inspected. C1 is resolved by the user's Minimal API decision. C2-C5 have design resolutions and no open objection; their dispositions are included in the approval request.
- **Pre-implementation review (2026-09-27, Main):** T1はVerified。T2のみRunnableからIn progressへ移し、T4/T5はDependent。active write ownerはgpt-6-luna worker一人で重複なし。既存domain HTTP testsを維持し、`/api/ml/train` filter範囲の回帰testを追加する。最小検証はAPI project buildと関連test、handoff前はAPI test suite、solution build、CI同等format verification。route/metadata/filter変更、public contract変更、scope拡大、検証失敗が一回のfocused correctionで閉じない場合はMainへ返す。
- **Checkpoint review (2026-09-27, Main):** focused HTTP slice detected 11 former-partial routes missing from facade; worker restored them and 29/29 focused tests passed. Full API suite then passed. Lead detailed review found broad unused-import blocks and inconsistent health operation naming; worker applied the focused cleanup and reran every gate.
- **Final review (2026-09-27, Main):** AC1-AC5 were reviewed as one integrated refactor group after all corrections. Route/name/tag/Produces/filter inventories match HEAD, endpoint/file/facade counts are 77, endpoint-to-endpoint handler calls are zero, CodeGraph resolves only Program and TestApplicationFactory callers, full independent verification passes, and no approved task or concern remains open. T2 quality/scope verdict is accept; requested model was gpt-6-luna, observed model and token usage are unavailable from runtime telemetry. One retry, one lead-requested correction, two review passes, and no known escaped defect were recorded. Fewer than five comparable samples means no persistent routing conclusion is made.

## Verification record

- Read-only CodeGraph exploration confirmed `MapApiEndpoints` callers and existing extraction seams.
- Repository inventory confirmed .NET 10, no MVC controller infrastructure, existing Minimal API feature mappers, and HTTP integration-test coverage by domain.
- Microsoft ASP.NET Core 10 documentation was checked for current Minimal API, route group, and controller guidance.
- No production code was changed during Design Mode.
- `codegraph sync .` — final production tree synchronized successfully; `MapApiEndpoints` callers remain `Program.cs` and `TestApplicationFactory.cs`.
- Route/metadata inventory against HEAD — route 77/77, `WithName` 66/66, `WithTags` 66/66, `Produces` 116/116; no differences. Three write-group filters occur once in the original order and five selective read filters remain.
- `dotnet build HorseRacingPrediction.sln --no-restore` — passed with 0 warnings and 0 errors (worker and independent lead rerun).
- `dotnet test tests/HorseRacingPrediction.Api.Tests/HorseRacingPrediction.Api.Tests.csproj --no-build` — 320 passed, 1 skipped, 0 failed (worker and independent lead rerun).
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — passed (worker and independent lead rerun).
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes --diagnostics IDE0005 --severity hidden --include src/HorseRacingPrediction.Api/Endpoints src/HorseRacingPrediction.Api/Extensions` — passed; extracted files contain only required imports.
- `git diff --check` — passed; Git emitted only existing LF-to-CRLF checkout warnings.
- `python .codex/skills/agent-task-orchestration/scripts/audit_agent_execution.py docs/changes/20260927_split-api-endpoints/README.md` — final result recorded below; repository-root validator wrapper was absent, so the skill-owned validator was used.

## Deviations and follow-up

- Initial focused verification found 11 omitted registrations from former partial mapper methods. They were restored before full-suite acceptance; no behavior deviation remains.
- Lead review required removal of mechanically copied unused imports and renamed `HealthEndpoint` to `GetHealthEndpoint`; both were reverified without behavior changes.
