# Align Playwright container and package versions

- Status: Implemented
- Change record schema: 2
- Owner: Collection platform maintainers
- Created: 2026-09-30
- Updated: 2026-09-30

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | Runtime imageを1.63.0へ更新し、static guardと両workflow wiring、app-deploy runtime smokeを追加した。 |
| Verification | Verified | Static positive/negative cases、YAML parse、container build、Playwright Chromium launch、既存deployment guard、formatter、diff checksが成功。 |
| Deployment/operation | Not applicable | Push、image publish、GitHub Actions再実行、production deploymentはscope外。 |

## Context

Collector Lambdaのbrowser起動が、`/ms-playwright/chromium_headless_shell-1243/...` 不在で失敗している。Playwrightの診断はpackage `1.63.0`に対してcontainer imageが`v1.62.0-noble`であると報告している。

Repository inspectionでも、`src/HorseRacingPrediction.Scraping/HorseRacingPrediction.Scraping.csproj`と`src/HorseRacingPrediction.Agents/HorseRacingPrediction.Agents.csproj`は`Microsoft.Playwright` `1.63.0`、`Dockerfile.collector-lambda`だけが`mcr.microsoft.com/playwright/dotnet:v1.62.0-noble`である。package update commit `a3149b95`はcsprojを更新したがDockerfileを更新していない。

## Goals

- Collector Lambda runtime imageのbrowser binariesをMicrosoft.Playwright package `1.63.0`と一致させる。
- 将来のpackage updateでDocker image更新が漏れた場合、container runtime failureより前にCIで検出する。
- 実際のbuilt image内でPlaywright Chromiumが起動できることを確認する。

## Non-goals

- Playwright API、browser behavior、scraping logicの変更。
- Playwright packageの追加upgrade/downgrade。
- Base image digest pinning policyの導入。
- Push、image publish、production deployment。

## Hypothesis ledger

| ID | Claim | Fact/inference boundary | Supporting and contradicting evidence | Falsification check and result | Disposition |
| --- | --- | --- | --- | --- | --- |
| H1 | Browser executable missing errorはpackage/image version mismatchで発生している。 | Error messageとrepository literalsはfact、sole cause classificationはinference。 | Errorはcurrent 1.62/required 1.63を明示。2 csprojは1.63、Dockerfileは1.62。別のmissing executable pathを示す反証なし。 | 全literalをrepository-wide検索し、version mismatchを再現可能なconfigurationとして確認。 | Confirmed; corrective premise。 |
| H2 | `v1.63.0-noble` imageは対象platformで利用可能。 | Registry manifest responseはfact。 | `docker manifest inspect`がlinux/amd64 digest `sha256:4bb37f...`とlinux/arm64を返した。 | MCR manifestをread-only取得しexit 0。 | Confirmed。 |
| H3 | 一行更新だけでは同種事故を防止しない。 | Process inference。 | Package update commitが2 package referencesを更新した一方、independent Docker literalが残った。既存CIはcontainerをbuildするがbrowser launchをしない。 | Workflowとtestsを検索し、package/image equality assertionがないことを確認。 | Confirmed; static guardとruntime smokeを追加。 |

## Legacy/current surface inventory

| Identifier | Surface | Classification | Approved disposition | Verification |
| --- | --- | --- | --- | --- |
| `mcr.microsoft.com/playwright/dotnet:v1.62.0-noble` | `Dockerfile.collector-lambda` runtime stage | Active mismatch | `v1.63.0-noble`へreplace | Repository literal searchとcontainer build/launch。 |
| `Microsoft.Playwright` `1.63.0` | Scraping csproj | Active canonical package version | Unchanged | Static guardがversionを抽出。 |
| `Microsoft.Playwright` `1.63.0` | Agents csproj | Active package version | Unchanged; Scrapingと一致を要求 | Static guardが全production csprojのversion一意性を検証。 |

Reconciliation: Playwright version-bearing active surfaces 3、replace 1、unchanged 2、unclassified 0。Historical change recordsの`1.62.0`記述は当時のevidenceでありhistory compatibilityとして変更しない。

## Documentation updates

- 本change recordを新規作成する。
- `docs/changes/20260925_playwright-tmp-enospc/README.md`と`docs/changes/20260915_playwright-collection-efficiency/baseline.md`の1.62.0記述を確認した。過去時点の調査・baselineであり書き換えない。
- Current architecture/operator documentationにPlaywright container versionをcanonicalに固定するものはなく、非change-record文書の更新は不要。

## Technical impact and decisions

1. `Dockerfile.collector-lambda` runtime imageを`mcr.microsoft.com/playwright/dotnet:v1.63.0-noble`へ変更する。
2. `tests/scripts/test-playwright-container-version.ps1`を追加する。全production csprojの`Microsoft.Playwright` versionが一意であること、Dockerfile tagがそのversionと完全一致すること、Ubuntu flavorが維持されることを検証する。
3. `app-ci`と`app-deploy`のverify jobでstatic guardを実行する。package-only PRでもguardが走る既存path filterを利用する。
4. Local verificationではcollector imageをbuildし、published applicationからPlaywright Chromiumをheadless起動して即終了するruntime smokeを行う。単なるimage build成功をbrowser availabilityの証拠にしない。

Rejected alternative: Docker imageだけ更新してguardを追加しない。今回と同じ独立literal driftを将来のNuGet updateで再発できるため不採用。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | Playwright packageとofficial imageは同一versionでなければbrowser revisionが一致しない。 | Build成功後のruntimeでのみbrowser起動が失敗し得る。 | Exact version equalityをstatic testで強制し、built imageでbrowser launchをsmoke testする。 | AC1-AC3/T1 | 推奨。 | 2026-09-30、設計全体を承認。 | Resolved in design |
| C2 | Full image build/pullは大きく、network/cache状態に依存する。 | Local verificationが外部要因で遅延・失敗し得る。 | Static guardを必須のdeterministic evidenceとし、manifest availabilityとruntime smokeも要求する。external network failureならAC3を未完了のまま報告し、成功を推測しない。 | AC2-AC3/T1 | 推奨。 | 2026-09-30、設計全体を承認。 | Resolved in design |
| C3 | Floating version tagは将来digestが変わり得る。 | Supply-chain reproducibilityは完全ではない。 | 今回はrepository既存policyに従いversion tagを維持。digest pinningは別scopeで、本不具合修正を阻害しない。 | AC1/T1 | 今回は除外を推奨。Owner: platform maintainers。 | 2026-09-30、version固定とCI一致検査の設計を承認。 | Excluded follow-up |

Open decisionと未解決のagent objectionはない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Collector runtime image tagが全production `Microsoft.Playwright` package version `1.63.0`と一致し、旧`v1.62.0-noble` active literalが残らない。 | T1 | Static version guardとrepository-wide literal inventory。 | Verified |
| AC2 | app-ci/app-deploy verifyがpackage/image mismatchをcontainer build前に失敗させる。 | T1 | Workflow source assertions、YAML parse、static guardのpositive/negative cases。 | Verified |
| AC3 | `Dockerfile.collector-lambda`からimageをbuildし、そのimage内でPlaywright Chromium headless launchが成功する。 | T1 | Docker buildとcontainer runtime smoke、期待exit 0。 | Verified |
| AC4 | Scraping/Agents behavior、production deployment、既存の無関係なworking-tree変更を変更しない。 | T1 | Scoped diff/status review、formatter、`git diff --check`。 | Verified |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Image version更新、static guard、workflow wiring、runtime smoke、record closureを行う。 | Main/Lead | High capability; 一つのversion invariantを同じDocker/workflow/test sliceで変更する小規模taskで、分割はreview/write coordination costを増やすため非委譲。 | User approval | `Dockerfile.collector-lambda`; `.github/workflows/app-ci.yml`; `.github/workflows/app-deploy.yml`; `tests/scripts/test-playwright-container-version.ps1`; 本README | Static positive/negative tests、YAML parse、Docker build/browser smoke、CI formatter、diff/status review。 | Commands/results、AC matrix、final reviewを本recordへ記録。 | Verified |

## Review gates

- **Design and task-split review (2026-09-30, Main/Lead):** Version-bearing active surface 3件を分類し、one replacementとtwo invariantsをACへ対応付けた。happy pathに加え、package version divergence、stale Docker tag、browser missing runtime counterexampleを検証対象にした。single invariant/single ownerのため非委譲。
- **Concern and agreement review (2026-09-30, Main/Lead):** Compatibility、external registry、supply-chain reproducibility、CI runtime cost、deployment boundary、secret/data riskを確認。C1-C3の処置でopen decisionなし。変更は公開API、data、credential、production mutationを含まない。
- **Pre-implementation review (2026-09-30, Main/Lead):** Userの「対応をお願いします」をAC1-AC4とC1-C3 dispositionへの明示承認として記録。T1は`In progress`、依存なし。exclusive write scopeはDockerfile、両workflow、version guard、本record。guardは実repository positive caseとsynthetic stale-image/package-divergence negative casesを実行する。runtime smokeはbuilt collector image内のpublished Playwright driverからChromiumをheadless起動する。外部contractまたはscope変更が必要なら`Proposed`へ戻す。
- **Checkpoint review (2026-09-30, Main/Lead):** AC1-AC3 groupをactive surface inventoryへ照合。Guardはactual repository、aligned fixture、stale-image、package-divergence、floating-imageを検証し、両workflow invocationを確認。Image build後、published Playwright Node driverからChromium 1243をheadless起動し、page content round-tripに成功。設計逸脱なし。
- **Final review (2026-09-30, Main/Lead):** T1とAC1-AC4は全てVerified。旧1.62 active literalは0件で、negative fixtureとhistorical documentsだけをintentional evidenceとして保持。diffはapproved five pathsに限定され、scraping/application behavior、package version、production stateを変更していない。未完了scope、open finding、acceptance-blocking external blockerなし。

## Verification record

Design evidence:

- Repository literals: production package references `1.63.0` x2、collector Playwright image `v1.62.0-noble` x1。
- `docker manifest inspect mcr.microsoft.com/playwright/dotnet:v1.63.0-noble --verbose` — exit 0、linux/amd64とlinux/arm64 manifestを確認。
- Existing CI builds the collector image but does not launch Playwright from that image; no package/image equality guard exists。

Implementation verification, 2026-09-30:

- `pwsh -NoProfile -File ./tests/scripts/test-playwright-container-version.ps1` — PASS。Actual alignment、aligned fixture、stale-image、package-divergence、floating-image、両workflow wiringを検証。
- Python `yaml.safe_load` for `app-ci.yml` and `app-deploy.yml` — PASS。
- `docker build -f Dockerfile.collector-lambda -t collector-lambda-playwright-163 .` — exit 0。Final image manifest `sha256:7301e0c8...`。
- Built image inspection — `/ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-linux64/chrome-headless-shell`存在。
- `docker run --entrypoint /var/task/.playwright/node/linux-x64/node ... chromium.launch(...)` — exit 0、headless browser launch、page creation/content round-trip、close成功。
- app-deploy相当のcached rebuildと同じruntime smoke — browser smokeまでPASS。続く既存core-suppression bind-mount commandはWindows checkoutのCRLFにより`set: Illegal option -\r`で失敗した。Linux runner checkoutではLFで実行される既存commandであり、今回のPlaywright smokeより後段のhost-only差としてACから除外。既存deployment guard 14 casesは別途全PASS。
- `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes` — exit 0。
- `git diff --check` — exit 0、line-ending conversion warningのみ。
- CodeGraph syncはproduction symbol/call relationshipを変更しないDocker/workflow/test/doc changeのため不要。

## Deviations and follow-up

承認済み設計からの逸脱なし。Docker Desktopは初回停止していたため起動後に再実行し、build/browser smokeは成功。Push、image publish、GitHub Actions dispatch、production deploymentは未実施。
