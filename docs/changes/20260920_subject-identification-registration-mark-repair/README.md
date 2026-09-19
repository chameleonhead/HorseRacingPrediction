# 主体識別情報の補正で競走馬登録区分表示を正規化する

- Status: Implemented
- Owner: Main
- Created: 2026-09-20
- Updated: 2026-09-20

## Completion summary

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Complete | repair preview/executeへ登録区分付き馬名の厳密な補正可能性判定を接続した。 |
| Verification | Complete | focused 7件、format、Release build、非External 1,145件成功、既知skip 1を確認した。 |
| Deployment/operation | Not applicable | 承認範囲はコード変更とローカル検証まで。本番applyは実施していない。 |

## Context

主体識別失敗には、保存名 `アジアエクスプレス` に対してJRAプロフィール見出しが
`マルガイ アジアエクスプレス` となる例がある。通常の収集・保存経路は共通の
`JraSubjectNameNormalizer` で `マルガイ`、`マル外`、`マル地`、`マルチ` と直後の空白を除去できる。

一方、主体識別情報の補正preview/executeは、失敗文言に「取得プロフィールの名前が一致しません」が含まれる場合を
一律に危険な保存済み根拠として扱う。そのため、既知登録区分を除去すれば完全一致する安全な失敗も自動的に補正可能と
判定できず、確認済みURLの再入力を要求する。

## Goals

- 主体識別情報の補正で、期待名と取得名の差が既知の競走馬登録区分表示と空白だけなら安全な補正候補にする。
- previewとexecuteで同じ共通normalizer・同じ安全判定を使用する。
- source identityとなる保存済みJRAプロフィールURLを維持してRecoveryを作成する。

## Non-goals

- 部分一致、類似度、読み、別名だけで同一馬と判定しない。
- 複数候補、URL欠落、JRA外URL、期待名・取得名を構造化して読み取れない旧エラーを自動補正しない。
- Horse aggregateが存在しない血統Discoveryの404問題はこの変更に含めない。
- 本番preview、apply、Recovery投入はこの変更の承認範囲に含めない。

## Documentation updates

- 本change recordのみ追加する。`docs/22-collector-design.md` と `docs/26-collection-platform-design.md` を確認したが、
  既知装飾の正規化と同一性根拠付き補正という既存方針は変更せず、補正経路の接続漏れを閉じるため正本文書更新は不要。

## Technical impact

- `SubjectNotIdentified` のプロフィール名不一致メッセージから期待名と取得名を厳密に抽出する。
- Resource種別がHorseで、両名を `JraSubjectNameNormalizer.NormalizeIdentityName("Horse", ...)` に通した結果が
  空でない完全一致となる場合だけ、既知登録区分による補正可能な不一致と判定する。
- 上記の場合は、保存済みの有効なJRA URLを補正根拠としてpreviewを `RetryReady` にできる。
- 抽出不能、正規化後不一致、非Horse、URL不正、複数統合候補等の既存block条件は維持する。
- executeでも同じ判定を再評価し、preview後に状態や証拠が変化した場合は既存どおりConflictで停止する。

## Decisions and material concerns

| ID | Concern and evidence | Impact | Disposition | State |
| --- | --- | --- | --- | --- |
| C1 | 現行normalizerは既に登録区分を除去するが、repairはエラー文言を一律unsafeにしている。 | 正しいURLが保存済みでも補正不能になる。 | repairの安全判定へ共通normalizerを接続する。 | Resolved in design |
| C2 | エラー文言の自由な部分一致は別馬を許可し得る。 | 誤ったHorseへプロフィールを保存する。 | 期待名・取得名を定型形式から抽出し、正規化後の完全一致だけを許可する。 | Resolved in design |
| C3 | `マルガイ`以外にも同じ意味のJRA表記がある。 | 表記ごとの個別実装が再発する。 | 既存normalizerが扱う `マルガイ`、`マル外`、`マル地`、`マルチ` と空白を共通適用する。 | Resolved in design |
| C4 | 対象Horseは現在404も発生している。 | 名前補正だけでは現行taskが成功しない可能性がある。 | Horse未作成問題は別課題として明示的に除外し、この変更を成功保証として扱わない。 | Accepted boundary |

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | Horseの期待名 `アジアエクスプレス` と取得名 `マルガイ   アジアエクスプレス` は、保存済みの有効なJRA URLがある場合に補正可能とpreviewされる。 | T1 | API preview test | Verified |
| AC2 | 同じ候補をexecuteすると、保存済みURLを維持したRecoveryが1件だけ作成または再利用される。 | T1 | API execute/idempotency test | Verified |
| AC3 | `マル外`、`マル地`、`マルチ` と全角・半角空白も共通normalizerの範囲で同じ判定になる。 | T1 | parameterized normalization tests | Verified |
| AC4 | 正規化後も異なる名前、抽出不能、非Horse、URL不正、複数候補は従来どおりBlockedまたはBadRequest/Conflictとなる。 | T1 | negative API tests | Verified |
| AC5 | 通常収集、主体名保存、曖昧候補の安全停止、既存repairの冪等性に回帰がない。 | T1 | focused/full regression gates | Verified |

## Delivery and task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | repairの名前不一致安全判定、preview/execute接続、回帰テスト、記録更新を実装する。 | Main | High capability | Approval | SubjectIdentificationRepair、API tests、本change record | AC1-AC5のfocused/full gates | diff、テスト結果、最終レビュー | Verified |

## Review gates

- **Design and task-split review — 2026-09-20, reviewer: Main.** 既存normalizer、repair preview/execute、
  2026-09-18の主体識別自動復旧record、本番アジアエクスプレスの旧失敗を照合した。単一の安全判定接続であり、
  identity判断を含むためMainが直列で担当する。AC1-AC5はpreview、execute、反例、回帰へ接続済み。
- **Pre-implementation review — 2026-09-20, reviewer: Main.** ユーザーの「お願いします」をAC1-AC5と明示した
  境界への承認として記録した。T1はRunnableからIn progressへ移行する。変更対象はrepair判定、API tests、change recordに限定し、
  本番applyとHorse未作成問題には触れない。定型メッセージを厳密に解析できない場合や反例テストが失敗する場合は安全側へBlockedを維持する。
- **Checkpoint review — 2026-09-20, reviewer: Main.** 定型エラーから期待名・取得名を抽出し、取得名に既知登録区分prefixがあり、
  共通normalizer適用後に空でない完全一致となる場合だけunsafe判定を解除した。previewとexecuteの両方が同じhelperを通ること、
  保存済みURL付きRecovery作成、異名とprefixなしの反例をfocused 7件で確認した。
- **Final review — 2026-09-20, reviewer: Main.** diffを承認済みAC1-AC5と照合した。非Horse、非構造化旧文言、
  公開識別子不一致、正規化後の異名はunsafeのままである。format、警告0のRelease build、非External全回帰が成功し、
  T1と全ACをVerifiedとした。本番applyとHorse未作成問題は承認境界どおり未実施・別課題であり、本変更の完了を阻害しない。

## Verification record

- 2026-09-20: 現行共通normalizerがHorse先頭の `マルガイ|マルチ|マル外|マル地` と後続空白を除去することを確認した。
- 2026-09-20: repairの `IsUnsafeStoredRepairEvidence` がプロフィール名不一致を内容にかかわらず一律unsafeとするため、
  共通normalizerによる補正可能性がpreview/executeへ接続されていないことを確認した。
- 2026-09-20: focused API test 7件が成功した。`マルガイ`候補のpreview、保存済みURL付きexecute、
  `マル外`、`マル地`、`マルチ`、半角・全角空白、異名、prefixなしを検証した。
- 2026-09-20: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`、
  Release solution build（警告0、エラー0）、非External全テストを実行し、1,145件成功、既知skip 1、失敗0を確認した。

## Deviations and follow-up

- 設計どおり実装した。本番applyは行っていない。血統Discoveryで参照先Horseを作らずtaskだけ作る404問題は別changeとして扱う。
