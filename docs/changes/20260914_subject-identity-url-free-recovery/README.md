# 主体識別の再収集でパラメーターなしURLを使用しない

- Status: Proposed
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

`/settings` の要対応データ補正は、現在、Horse/Jockey/Trainer/Ownerの `SubjectNotIdentified` を再実行する際に、パラメーターを含むJRA URLを必須としている。このため `accessR.html`、`accessK.html`、`accessS.html`、`accessD.html` などの選択ページURLだけが記録された失敗は入力エラーになる。

これらは主体を識別するURLではない。拒否対象として運用者に直させるのではなく、収集locationから除外し、元のRaceEntryとジョブ属性を正本としてURLを指定しないDiscovery/Recoveryを行う。

## Goals

- パラメーターなしのJRA `access*.html` を主体のlocation、source identity、補正URLとして使用・保存しない。
- URLがなくても、競走馬・騎手・調教師は名前、生年月日、主体種別などのジョブ属性からJRAの公式プロフィールを探索する。
- 馬主は外部プロフィールURLを要求せず、RaceEntryの馬主名スナップショットから既存のOwner解決経路を実行する。
- `/settings` から補正URLを入力せずRecoveryを作れるようにする。
- URLを使用しないRecoveryでも、同名競合や必要属性不足を成功扱いにしない。

## Non-goals

- JRAサイトへのHTTPアクセスを全面的に廃止すること。Horse/Jockey/Trainerのプロフィール探索はNavigatorがJRA内を遷移する。
- 名前だけを根拠に別主体同士を名寄せすること。
- パラメーター付きで主体を一意に示す公式プロフィールURLを禁止すること。
- RaceEntryに存在しない主体名を推測すること。

## Proposed behavior

### URLの分類

- query parameterを持たないJRA `access*.html` は「無効入力」ではなく「主体locationではない」と分類して破棄する。
- パラメーター付きURLでも、主体ページとして一意に解釈できないものはDiscoveryの制約に使わない。
- Horse/Jockey/Trainerの正規プロフィールURLが検証できた場合は、高精度なsource identityとして引き続き使用できる。
- 画面はURL入力を必須にせず、「URLなしで再収集」を主操作にする。利用可能な正規プロフィールURLがある場合だけ補助情報として表示する。

### 主体別のURLなし取得

- Horse: RaceEntryまたは既存ジョブの馬名・生年月日を `JraSubjectIdentity` に渡し、`ToSubjectProfileAsync` で探索する。同定できたプロフィールのterminal URL/source identityだけを結果として保存する。
- Jockey: RaceEntryまたは既存ジョブの騎手名を用いて同じDiscoveryを行い、取得ページの氏名一致を検証する。
- Trainer: RaceEntryまたは既存ジョブの調教師名を用いて同じDiscoveryを行い、取得ページの氏名一致を検証する。
- Owner: RaceEntry由来の馬主名を正規化し、既存Owner/alias mappingへ解決する論理収集とする。外部プロフィール本文やURLは要求しない。同名競合または名前欠落は `SubjectNotIdentified` とする。

### Recoveryと名寄せ

- `/settings` のexecute APIは補正URLを任意にし、URLなしでは元failureのresource/definitionをRecoveryとして再要求する。
- 過去のparameterless URLはRecovery requestへ引き継がない。
- Horseの安全な既存merge candidateがある場合だけ、従来どおりtargetをRecoveryしsourceを抑止する。
- merge candidateが0件の場合は名寄せせず、同一resourceのRecoveryを行う。

## Decisions

1. parameterless `access*.html` は入力エラーではなく無視対象とする。
2. 「URLを使わない」は、運用者入力・永続location・source identityに選択ページURLを使わない意味とする。サイト内探索そのものは継続する。
3. OwnerはRaceEntryの名前スナップショットを入力にした内部identity解決とし、JRAプロフィールURLを導入しない。
4. URLなしDiscoveryは名寄せ根拠にしない。既存の同一JRA識別子または同一RaceEntry根拠が一意な場合だけmergeする。

## Acceptance criteria

| ID | Observable criterion | Verification | State |
|---|---|---|---|
| AC1 | parameterless `accessR/K/S/D` および同種のJRA `access*.html` が既存locationまたは画面入力にあっても、エラーにせず無視してRecoveryできる。 | handler/API integration tests | Proposed |
| AC2 | Horse/Jockey/Trainerは補正URLなしで、保存済みの名前等からDiscoveryを実行し、取得したプロフィールの主体種別・名前を検証する。 | subject handler tests | Proposed |
| AC3 | Ownerは補正URLなしでRaceEntry由来の馬主名を解決し、プロフィール本文を取得・保存しない。名前欠落・競合は識別失敗になる。 | owner identity tests | Proposed |
| AC4 | `/settings` はURL入力を必須にせず、4主体の実行可能なfailureを「URLなしで再収集」できる。 | bUnit/API tests | Proposed |
| AC5 | URLなしRecoveryではsourceを抑止しない。安全なHorse merge時だけtargetをRecovery後にsourceを抑止する。 | endpoint/Collection Platform tests | Proposed |
| AC6 | parameterless URLを新しいrequest location、成功source identity、attemptのRequestedUrlとして再利用しない。 | persistence integration tests | Proposed |
| AC7 | 同名競合、主体名欠落、またはHorseの複数merge targetはBlocked/SubjectNotIdentifiedとなり、自動名寄せされない。 | conflict tests | Proposed |
| AC8 | 既存のパラメーター付き正規プロフィールURLによる収集と、従来Horse repairのredirect/監査は回帰しない。 | regression tests | Proposed |

## Documentation updates

- `docs/changes/20260914_subject-identity-repair-jobs/README.md`: 実装済み機能の後続変更として本recordを参照し、parameterless URLの扱いを「拒否」から「無視」へ置き換える。
- `docs/26-collection-platform-design.md`: locationの適格性とURLなしsubject Discovery/RecoveryをCollection Platformの正本へ反映する。現在、別作業の未コミット変更があるため、承認後に競合しないhunkとして更新する。
- `docs/20-admin-ui-design.md`: `/settings` の補正URLを任意入力へ変更する。現在、別作業の未コミット変更があるため、承認後に競合しないhunkとして更新する。

## Delivery plan

1. parameterless/非terminal JRA URLを主体locationから除外する共通判定を追加する。
2. Subject handlerとRecovery APIをURL任意に変更する。
3. OwnerをRaceEntry属性から解決するURLなしidentity handlerへ変更する。
4. `/settings` の表示・実行をURL任意へ変更する。
5. focused/full regression、format、build、CodeGraph、change recordを検証する。

## Review gates

- **Design and task-split review** — Main。現行handler/API/RaceEntryモデルを確認。URL分類、3主体の既存name Discovery、Ownerの論理収集、UIのURL任意化は順序依存があるためMainが直列実装する。AC1〜AC8にproduction pathとテストを割り当てた。
- **Pre-implementation review** — 承認後に記録する。
- **Checkpoint review** — 実装checkpointで記録する。
- **Final review** — 全ACをVerifiedへ照合後に記録する。

## Verification record

- 2026-09-14: CodeGraphと実コードで、現行Horse/Jockey/Trainer handlerはlocation失敗後にname Discoveryへ移る一方、Ownerは`SupportsNameDiscovery=false`のためlocationなしで識別失敗になることを確認した。
- 2026-09-14: 現行repair API/UIはquery parameter付きJRA URLを必須とし、parameterless `access*.html` をBadRequest/Blockedにすることを確認した。

## Deviations and follow-up

- Production codeは未変更。ユーザー承認後に実装する。
