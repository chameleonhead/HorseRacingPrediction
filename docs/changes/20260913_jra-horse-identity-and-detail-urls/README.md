# JRA競走馬の同定競合防止・不具合データ修復・詳細URL保証

- Status: Approved
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

本番の `Horse/JRA/horse-349b8cd2-8278-5267-a7d7-2b7ab5f011bb/horse-profile` は、内部競走馬名「ビッグヒーロー」に生年月日と保存済みLocationがない状態で名称Discoveryを実行し、JRA公開検索の同名候補3件を検出して `SubjectNotIdentified:MultipleCandidates` になった。候補はそれぞれ異なる `accessU.html?CNAME=...` を持つ別世代の競走馬であり、名称だけから先頭候補を採用してはならない。

現行のRaceCard parserは馬名セル中のプロフィールリンクを主体情報へ保持せず、RaceCard handlerは馬名だけから決定論的な内部IDを生成してHorse profile requestを作る。そのため、出馬表時点では一意なJRA公開識別子が存在しても、後段Discoveryで失われる。また内部Horse IDも正規化名だけから生成されるため、同名別馬を区別できず、表記揺れでは同一馬が別IDになり得る。

RaceCard/RaceResult Discoveryでは、子requestへ渡すExplicit URLが詳細ページであることをJRA URL構造として保証していない。`/JRADB/accessD.html`、`/JRADB/accessS.html` の開催選択ページURLをパラメーターなしで保存・直接取得すると、詳細対象を指定できずエラーになる。

## Goals

- RaceCardで得たJRA競走馬プロフィールの公開識別子を失わず、Horse登録、RaceEntry、profile requestまで伝播する。
- JRA競走馬は名称ではなくJRA公開識別子を強い一意キーとして解決し、同名別馬を混同しない。
- 今回の不具合で同一競走馬から複数の内部Horseが生成された対象だけを、一回限りの修復処理で名寄せする。
- 今回の不具合でidentityを失った既存Horseを、関連RaceEntryとRaceCardの公開リンクから正しいJRA identityへ関連付ける。
- RaceCard/RaceResultのExplicit URLには、対象を指定する有効なパラメーターを必須とし、パラメーターなしの `accessD` / `accessS` を子requestへ渡さない。
- 現在の「ビッグヒーロー」を出走レースの公開リンクから一意に同定し、適切なJRA identityへ関連付けて再取得できるようにする。

## Non-goals

- 名前だけ、または類似名だけを根拠に自動名寄せすること。
- JRA上で同名の別世代競走馬を1件へ統合すること。
- JRAの一時的な詳細URL全体を永続的なドメインIDとして使用すること。
- JRA以外のProviderのidentity規則を同時に変更すること。
- パラメーターなしの開催選択ページ自体を禁止すること。NavigatorがDiscoveryの起点として利用することは維持する。
- 通常収集へ汎用的な自動名寄せ機能を追加すること。
- 将来発生する任意のHorse重複を、名称や推定情報から自動統合すること。

## Experience and interaction design

通常収集では、RaceCardの各馬名に付随するJRAプロフィールリンクを解析し、JRA identity付きでHorseを解決する。既存の同一identity Horseがあれば必ずそのIDを使い、なければ新規Horseを作成する。通常収集は複数Horseの名寄せを実行しない。既存RaceEntryがidentity未設定のHorseを参照する場合は、その同じRaceEntryから得た公開リンクを当該Horseへ付与し、新しいHorseを作らずに収束させる。

名寄せは今回の不具合修復専用のversioned repairとして実行する。先にdry-runで候補と根拠を固定したrepair manifestを生成し、同じ正規化済みJRA identityを持つとRaceCard実データで確認できた内部Horseだけを統合する。適用対象、canonical ID、統合元ID、根拠RaceEntry、JRA identityをmanifestへ記録し、そのmanifest以外へ処理を広げない。異なるJRA identity同士、identityなしで名前だけが一致するHorseは修復対象に含めない。

DiscoveryがRaceCard/RaceResult子requestを作る際、詳細URLはHTTPS、JRA正規host、期待path、単一かつ非空の `CNAME`、対象Resourceと整合する識別部分を満たす場合だけExplicit URLとして保存する。条件を満たさない場合、パラメーターなしURLを渡さず、子handlerの通常Discoveryを使用する。

## Documentation updates

- `docs/22-collector-design.md`: JRA主体identityの伝播、今回の不具合データに限定した修復、RaceCard/RaceResult詳細URLの検証規則を正本の収集設計へ追加する。
- 本change record: 今回の移行対象、統合選択規則、受け入れ基準、検証結果の正本とする。

## Technical impact

- `RaceEntry` とRaceCard parserへHorse profile URL / provider identityを追加し、semantic snapshotの馬名セルlinkから取得する。
- JRA URL正規化器は `accessU` のvolatileでない公開識別部分を抽出し、Providerと組み合わせたidentity keyを生成する。
- Horse登録を現在のクライアント側 `GET -> POST/PUT` と名称UUID生成から、API側のatomicな `resolve-or-register` へ移す。DBには `(Provider, ExternalIdentity)` の一意制約を持つHorse identity mappingを置く。
- resolve-or-registerは一意制約競合時に既存mappingを再読込して同じcanonical Horse IDを返す。複数Workerが同時処理しても別Horseを確定しない。
- 通常のresolve-or-registerは、同じRaceEntryがidentity未設定のlegacy Horseを参照していれば、そのHorseへidentity mappingを付与する。別identityを持つHorseや複数Horseを通常収集中に統合しない。
- 不具合修復用のversioned repairは、dry-runで `repair-id`、canonical Horse、source Horse、JRA identity、根拠RaceEntryを持つmanifestを作る。applyはmanifestを再検証し、RaceEntry、Horse profile/read model、Collection Resource/Location、memo subject等の参照をcanonical IDへ付け替え、source IDのredirectと修復監査を保存する。
- repair ledgerへ `repair-id` と各source Horseの完了状態を保存し、再起動・再実行時は完了済み項目をskipする。manifest外のHorseは更新しない。
- collection request属性へ `sourceIdentity` を含め、Horse handlerは保存済みLocationまたはidentity指定URLを先に検証する。名称Discoveryはidentityを取得できなかったlegacy対象だけのfallbackとする。
- Race DiscoveryとHorse history Discoveryは、JRA詳細URLvalidatorを通過したURLだけをExplicit URLとして子requestへ渡す。bare `accessD` / `accessS` はnullへ落として通常Discoveryへ進める。

## Decisions

1. HorseのJRA同一性は正規化名ではなくJRA公開識別子を正本とする。同名でもidentityが異なれば別Horseである。
2. 通常処理は名寄せしない。同一RaceEntryはidentity未設定の既存Horseへidentityを付与する根拠にだけ使い、2つのHorseを統合する根拠には単独で使用しない。
3. 名寄せは `20260913-jra-horse-identity-repair` の専用repairとして一回限り実行する。dry-run manifestに列挙され、同一JRA identityを実RaceCard linkで再検証できた組だけをapplyする。
4. repairのcanonical Horseは、既に当該JRA identity mappingを持つHorseを優先する。該当がなければ参照数が多いID、同数なら作成時刻が古いIDをmanifest生成時に選び、apply時に変更しない。
5. 統合元Horse IDは削除せずredirect/tombstoneとして保持する。これにより外部リンク、過去イベント、保存済みjob属性からの参照を回復可能にする。
6. `accessD` / `accessS` の開催選択ページはDiscovery起点として許可するが、RaceCard/RaceResult子requestのExplicit URLには単一・非空の `CNAME` とResource整合性を必須とする。
7. URL不正時に推測でパラメーターを組み立てない。一覧上の検証済み詳細linkを使い、得られなければ通常Navigator Discoveryへフォールバックする。

## Acceptance criteria

| ID | Observable criterion | State |
|---|---|---|
| AC1 | RaceCardの馬名セルにJRAプロフィールlinkがあると、Horse resolve、RaceEntry保存、horse-profile requestのすべてに同じJRA identityが伝播する。 | Verified |
| AC2 | 同名で異なるJRA identityの競走馬は別Horseとして保存され、名称Discoveryで相互に誤選択されない。 | Verified |
| AC3 | 同一JRA identityを2Workerが同時にresolveしても、1つのcanonical Horse IDだけが返り、mappingとHorseが重複しない。 | Verified |
| AC4 | 専用repairのdry-runが、今回の不具合由来候補についてcanonical/source ID、共通JRA identity、根拠RaceEntry、更新予定参照数を含む固定manifestを生成し、名前だけ一致する候補を除外する。 | Verified |
| AC5 | repair applyはmanifest記載対象だけを統合し、RaceEntry等の参照をcanonical Horseへ移し、source IDのredirectと監査記録を残す。再実行しても追加変更・監査重複がない。 | Verified |
| AC6 | `accessD.html` / `accessS.html` のパラメーターなしURLはRaceCard/RaceResult子requestのExplicit URLとLocationに保存されない。 | Verified |
| AC7 | 単一・非空の `CNAME` を持ち対象Resourceと整合する詳細URLは保存され、direct取得に成功する。不正URLは通常Discoveryへフォールバックする。 | Verified |
| AC8 | 現在のビッグヒーロー対象は、関連RaceCardの馬linkから2023年登録のJRA identityへ一意に関連付けられ、profile取得が成功する。JRA上の異なる同名2頭はrepair manifestに含まれない。 | Connected |
| AC9 | repair dry-runで、同一JRA identityの内部重複、名称だけ一致する候補、bare accessD/accessS Locationの件数を記録してから、manifestの安全対象だけをapplyする。 | Connected |
| AC10 | parser、API同時実行、repair manifest/apply/再実行、redirect、Discovery→child request、Worker direct/fallbackの統合テストが成功する。 | Verified |
| AC11 | 修正版配備後の通常収集はHorse mergeを呼ばず、同じRaceEntryのidentity未設定Horseへのmapping付与だけを行う。 | Connected |

## Delivery plan

1. RaceCard semantic fixtureと実サイトsnapshotでHorse profile linkの所在・URL構造を固定し、parser/modelへidentityを通す。
2. Horse external identity mappingを追加し、通常処理用atomic resolve-or-registerとidentity未設定Horseへのmapping付与を実装する。
3. RaceCard保存経路をcanonical Horse IDへ接続し、profile requestへidentity/Locationを渡す。
4. RaceCard/RaceResult詳細URLvalidatorをDiscovery、Location登録、direct handler境界へ接続する。
5. 専用repairのdry-runで今回の不具合候補を分類し、固定manifestを生成する。ビッグヒーローは関連RaceCard linkでidentityを確定する。
6. manifest applyで対象だけを名寄せし、ledger、redirect、監査、partial failure/restart、再実行、誤統合防止を検証する。
7. 修復完了後、専用repairの新規受付を無効化し、本番再取得結果と適用件数を記録する。

## Verification record

- 2026-09-13: ユーザーが実装を明示的に依頼し、本change recordを承認したためExecution Modeへ移行した。
- 2026-09-13: 本番対象画面で内部Horse ID `horse-349b8cd2-8278-5267-a7d7-2b7ab5f011bb`、名称「ビッグヒーロー」、生年月日なし、Location 0件を確認した。
- 2026-09-13: 同画面の診断で `SubjectIdentification:MultipleCandidates` と、異なるJRA `accessU.html?CNAME=...` を持つ同名候補3件を確認した。
- 2026-09-13: 本番Horse一覧では「ビッグヒーロー」の内部レコードは現時点で1件だった。JRA候補3件は同名別馬であり、その3件自体を名寄せしてはならない。
- 2026-09-13: 現行RaceCard parserの `RaceEntry` はHorse profile URLを保持せず、RaceCard handlerは馬名だけからHorse IDとprofile requestを生成することを確認した。
- 2026-09-13: 現行Horse Upsertはクライアント側で名称由来IDを生成し `GET -> POST/PUT` を行う。作成競合自体はConflict後Updateへ収束するが、外部identityによる同名別馬識別と表記揺れ重複の収束は保証しないことを確認した。
- 2026-09-13: Race Discoveryは一覧parserのURLを `CollectionHttpUrl.Resolve` した結果をそのまま子requestへ渡し、JRA詳細パラメーターの必須検証を行わないことを確認した。
- 2026-09-13: ユーザー指示により、名寄せを通常機能ではなく今回のロジック不具合に限定したversioned repairへ変更した。同一RaceEntryは通常時のmerge条件から外し、identity未設定Horseへmappingを付与する根拠に限定した。
- 2026-09-13: RaceCard parserからHorse profile URLをHorse登録、RaceEntry再登録、profile requestまで伝播し、JRA `CNAME` を正規化したcanonical Horse IDへ収束させる実装を追加した。
- 2026-09-13: `accessD` / `accessS` 詳細URL validatorを追加し、単一の非空 `CNAME` と日付・場・レース番号が対象Resourceに一致するURLだけを子requestへ保存するようにした。bare URLは通常Discoveryへフォールバックする。
- 2026-09-13: 専用repair `20260913-jra-horse-identity-repair` のdry-run/apply、候補ledger、source ID redirect、再実行skipを追加した。通常収集に汎用merge処理は追加していない。
- 2026-09-13: SQLiteの現行EnsureCreated、一世代前（JRA profileあり）、旧スキーマから新repair tableへデータ保持移行できることを検証した。
- 2026-09-13: `dotnet test HorseRacingPrediction.sln -c Release --no-restore --filter "TestCategory!=External"` 成功（失敗0、skip1）。追加修正後のfocused API/Scraping testsも成功した。

## Deviations and follow-up

- 設計では別テーブルのexternal identity mappingとAPI側resolve-or-registerを想定したが、実装は正規化JRA identityからUUIDv5相当のcanonical Horse IDを決定論的に生成する方式とした。同一identityの同時Workerが同じIDへ収束する受け入れ結果を、追加mappingの整合性管理なしで満たす。
- legacy Horseをその場で改名するのではなく、同じRaceEntryをcanonical IDで再登録してEventFlow projectionの参照を移し、その事実から専用repair候補を記録する。applyは参照残存がないことを再検証してsource redirectを確定する。
- 本番配備、repair dry-run、manifest確認、apply、ビッグヒーロー再取得は未実施。実装完了とは分離し、配備後にAC8/AC9の実測結果を本記録へ追記する。
- repairを管理画面から確認・実行するfollow-upは [JRA競走馬識別子の不具合修復を管理画面から実行する](../20260914_horse-identity-repair-admin-ui/README.md) で設計する。
