# 収集監視の要対応判定をJSON列挙値表現から分離する

- Status: Proposed
- Change record schema: 2
- Owner: Main
- Created: 2026-09-19
- Updated: 2026-09-19

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Not started | 利用者承認後、APIへ安定したboolean契約を追加しworkflowを切り替える。 |
| Verification | Not started | endpoint JSON fixtureとworkflow契約テストが必要。 |
| Deployment/operation | Not started | deploy後、High findingでannotationが生成されることをshadow確認する。 |

## Incident summary

2026-09-19 22:13 JSTのproduction monitoring run `35445129086` は、High findingを含むためAPI上の `CollectionMonitoringOutcome.ActionRequired` を返した。しかしJSONではenum既定表現の数値 `2` となり、GitHub Actionsは文字列 `"actionRequired"` と比較したため `action_required=false` と判定し、`Mark actionable monitoring result` をスキップした。

- Production run: https://github.com/chameleonhead/HorseRacingPrediction/actions/runs/35445129086
- API evidence: `outcome=2`、High findingあり、finding count 49、`truncated=true`
- Workflow consumer: `.github/workflows/collection-monitoring.yml` の `jq '.outcome == "actionRequired"'`
- Operational effect: probeとchange-record生成は成功したが、GitHub Actionsの要対応warning annotationが生成されなかった。

秘密情報、未編集ログ、API keyは本recordへ保存しない。

## Root cause boundary

service内のseverity判定はHigh/Criticalを `ActionRequired` に正しく分類している。欠陥は、APIとworkflowの境界に安定したwire contractがなく、C# enumの型付き値をworkflowが未検証の文字列表現として仮定した点にある。service単体テストはenum値を直接検証していたため、実際のJSON表現とworkflow consumerの不一致を検出できなかった。

## Goals

- workflowがserializerのenum表現に依存せず、要対応状態を確実に判定する。
- typed outcomeは既存利用者向けに保持し、明示的なboolean wire contractを追加する。
- productionと同じHTTP JSONからworkflow判定までをfixtureで検証する。
- High/CriticalまたはUnexpectedPipelinePauseのprobe成功時にwarning annotationを生成する。

## Non-goals

- findingのseverity、fingerprint、最大件数、truncation契約を変更しない。
- workflow成功を失敗へ変更しない。要対応状態と監視実行失敗の区別を維持する。
- pipeline resume、task再実行、failure解決、データ補正を行わない。
- 通知sink、acknowledgement、30分heartbeatの設計を変更しない。

## Proposed design

`CollectionMonitoringReport` のJSONへ、`Outcome == ActionRequired` から導出する読み取り専用boolean `actionRequired` を追加する。既存の `outcome` は後方互換のため維持する。GitHub Actionsは `.actionRequired == true` のみをconsumer contractとし、summaryにもAPIが返したbooleanをそのまま記録する。

API endpoint testは実際のJSONを読み、High finding fixtureで `actionRequired=true`、medium-only/healthy fixtureでfalseを検証する。workflow contract testはproduction形状のJSONを入力し、`outcome` が数値のままでもbooleanからannotation stepが有効になることを検証する。未知または欠落したbooleanは安全側でworkflowを失敗させ、黙ってfalseにしない。

## Documentation updates

- `docs/changes/20260919_collection-monitor-notification-escalation/README.md` を既存の通知契約の正本として確認した。要求を変更せず実装欠陥を閉じるため更新しない。
- `docs/11-automation-design.md` と `docs/26-collection-platform-design.md` を確認した。外部挙動の変更はなく、追加更新は不要。
- 本recordを欠陥、修正境界、検証条件の正本とする。

## Technical impact

- `src/HorseRacingPrediction.Api/CollectionController/CollectionMonitoringService.cs`: report DTOへ安定したbooleanを追加する。
- `.github/workflows/collection-monitoring.yml`: enum文字列比較をboolean consumerへ置き換え、欠落時をfail-closedにする。
- `tests/HorseRacingPrediction.Api.Tests/CollectionMonitoringServiceTests.cs` またはendpoint契約テスト: HTTP JSON表現を検証する。
- workflow YAML/fixture test: production形状の数値enum JSONでannotation条件を検証する。

## Decisions

- API全体のenum serializerを文字列へ変更しない。影響範囲が広く、既存consumerの後方互換リスクがある。
- workflowで数値 `2` を直接比較しない。enumの順序へ新しい暗黙依存を作るためである。
- additive booleanを境界契約とし、serviceのtyped outcomeから一意に導出する。

## Concern and agreement ledger

| ID | Concern and evidence | Impact | Proposed disposition | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | production JSONは`outcome=2`、workflowは文字列を期待した。 | High異常でもannotationが黙って欠落する。 | 明示booleanを追加し、欠落はfail-closedにする。 | AC1-AC3/T1/API+workflow fixture | Agree | Pending | Resolved in design |
| C2 | API全体をstring enum化すると他endpoint/clientへ波及し得る。 | 監視修正が無関係なconsumerを破壊する可能性。 | outcomeを維持するadditive変更に限定する。 | AC4/T1/compatibility fixture | Agree | Pending | Resolved in design |
| C3 | 今回のreportは`truncated=true`だった。 | finding全件の可視性は別途制約される。 | actionabilityはtruncation前後で安全側に保持し、本変更では上限契約を変更しない。truncation改善は別owner Mainのfollow-upとする。 | AC2/T1/high+truncated fixture | Agree | Pending | Excluded follow-up |

Open decisionはない。変更は加算的かつ可逆で、データ移行・破壊操作・権限変更を含まない。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | High/CriticalまたはUnexpectedPipelinePauseのHTTP reportが`actionRequired=true`を返す。 | T1 | endpoint JSON fixtures | Not started |
| AC2 | `outcome`が数値で`truncated=true`でもworkflowがbooleanを読み、warning annotation stepを実行する。 | T1 | production-shaped workflow fixture/YAML assertion | Not started |
| AC3 | `actionRequired`が欠落またはboolean以外ならworkflowは明示失敗し、falseとして継続しない。 | T1 | negative workflow fixtures | Not started |
| AC4 | 既存`outcome` fieldとhealthy/medium-only判定を維持し、既存consumer契約を壊さない。 | T1 | endpoint compatibility tests | Not started |
| AC5 | deploy後のproduction shadow runでHigh finding時のwarning annotationを確認する。 | T2 | GitHub Actions run evidence | Not started |

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | additive boolean契約、workflow fail-closed consumer、境界fixtureを実装する。AC1-AC4 | Main | Lead tier | Approval | API DTO, workflow, focused tests | endpoint/workflow tests, format, build, diff check | commit and test output | Proposed |
| T2 | deploy後にHigh production reportからannotationまでをshadow確認する。AC5 | Main + operator | Lead tier | T1 deploy | Read-only production evidence | workflow run inspection | run URL and observation | Dependent |

## Review gates

- **Design and task-split review — 2026-09-19, reviewer: Main.** production JSON、workflow log、service enum判定、既存notification recordを照合した。public contractとconsumerは同じ境界変更でありT1を単一ownerとし、production shadowのみT2へ分離する。
- **Concern and agreement review — 2026-09-19, reviewer: Main.** compatibility、通知欠落、truncation、rollback、秘密情報、外部権限を確認した。additive booleanとfail-closed consumerでmaterial concernを解消し、truncation自体はACを妨げない別follow-upとした。Open decisionはない。
- **Pre-implementation review:** 利用者承認後に実施する。承認前のproduction code変更は禁止する。
- **Checkpoint review:** T1の実diff、HTTP JSON、workflow fixture、CI相当gateを照合する。
- **Final review:** AC1-AC5とproduction shadowを照合し、全taskがVerifiedになるまでImplementedにしない。

## Verification record

- 2026-09-19 22:13 JST: run `35445129086` で `outcome=2`、High finding、`action_required=false`、annotation step skippedを確認した。
- 2026-09-19 22:16 JST: serviceはHigh/CriticalまたはUnexpectedPipelinePauseをtyped `ActionRequired` に設定すること、workflowだけが文字列比較していることを確認した。
- 提案のみであり、コード変更、deploy、pipeline操作、task再実行、データ補正は行っていない。

## Deviations and follow-up

- 既存監視はCodex heartbeat出力で要対応を継続通知する。workflow annotation欠落の恒久修正は本recordの承認待ち。
- truncation上限と全finding可視性の改善は本変更のACを妨げない別follow-upとしてMainが追跡する。
