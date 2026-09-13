# JRA競走馬の同定競合防止・自動名寄せ・詳細URL保証

- Status: Proposed
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
- 同一JRA公開識別子が複数の内部Horseに結び付いていることを確認した場合、安全な規則で自動名寄せする。
- 既存の名称由来Horseを、同じRaceEntryから得たJRA公開識別子付きHorseへ安全に収束させる。
- RaceCard/RaceResultのExplicit URLには、対象を指定する有効なパラメーターを必須とし、パラメーターなしの `accessD` / `accessS` を子requestへ渡さない。
- 現在の「ビッグヒーロー」を出走レースの公開リンクから一意に同定し、適切なJRA identityへ関連付けて再取得できるようにする。

## Non-goals

- 名前だけ、または類似名だけを根拠に自動名寄せすること。
- JRA上で同名の別世代競走馬を1件へ統合すること。
- JRAの一時的な詳細URL全体を永続的なドメインIDとして使用すること。
- JRA以外のProviderのidentity規則を同時に変更すること。
- パラメーターなしの開催選択ページ自体を禁止すること。NavigatorがDiscoveryの起点として利用することは維持する。

## Experience and interaction design

通常収集では、RaceCardの各馬名に付随するJRAプロフィールリンクを解析し、JRA identity付きでHorseを解決する。既存の同一identity Horseがあれば必ずそのIDを使い、なければ新規Horseを作成する。同じRaceEntryが従来の名称由来Horseを参照している場合は、収集処理内でidentity付きHorseへ名寄せしてRaceEntryを正本へ付け替える。

同一JRA identityが複数Horseへ結び付く矛盾を検出した場合は、強い証拠が一致しているため自動名寄せする。統合元IDはredirectとして残し、詳細画面/API参照は統合先を返す。異なるJRA identity同士、identityなしで名前だけが一致するHorseは自動統合せず、要対応候補として記録する。

DiscoveryがRaceCard/RaceResult子requestを作る際、詳細URLはHTTPS、JRA正規host、期待path、単一かつ非空の `CNAME`、対象Resourceと整合する識別部分を満たす場合だけExplicit URLとして保存する。条件を満たさない場合、パラメーターなしURLを渡さず、子handlerの通常Discoveryを使用する。

## Documentation updates

- `docs/22-collector-design.md`: JRA主体identityの伝播、強いidentityに限定した自動名寄せ、RaceCard/RaceResult詳細URLの検証規則を正本の収集設計へ追加する。
- 本change record: 今回の移行対象、統合選択規則、受け入れ基準、検証結果の正本とする。

## Technical impact

- `RaceEntry` とRaceCard parserへHorse profile URL / provider identityを追加し、semantic snapshotの馬名セルlinkから取得する。
- JRA URL正規化器は `accessU` のvolatileでない公開識別部分を抽出し、Providerと組み合わせたidentity keyを生成する。
- Horse登録を現在のクライアント側 `GET -> POST/PUT` と名称UUID生成から、API側のatomicな `resolve-or-register` へ移す。DBには `(Provider, ExternalIdentity)` の一意制約を持つHorse identity mappingを置く。
- resolve-or-registerは一意制約競合時に既存mappingを再読込して同じcanonical Horse IDを返す。複数Workerが同時処理しても別Horseを確定しない。
- identity付き登録後、同じRaceEntryが参照するlegacy Horseが別IDなら名寄せ候補とし、JRA identity一致または同一RaceEntry由来という強いprovenanceをtransaction内で検証して統合する。
- Horse mergeはRaceEntry、Horse profile/read model、Collection Resource/Location、memo subject等のHorse参照をcanonical IDへ付け替え、source IDのredirectと監査記録を保存する。再実行は冪等にする。
- collection request属性へ `sourceIdentity` を含め、Horse handlerは保存済みLocationまたはidentity指定URLを先に検証する。名称Discoveryはidentityを取得できなかったlegacy対象だけのfallbackとする。
- Race DiscoveryとHorse history Discoveryは、JRA詳細URLvalidatorを通過したURLだけをExplicit URLとして子requestへ渡す。bare `accessD` / `accessS` はnullへ落として通常Discoveryへ進める。

## Decisions

1. HorseのJRA同一性は正規化名ではなくJRA公開識別子を正本とする。同名でもidentityが異なれば別Horseである。
2. 自動名寄せは、同一JRA identity、または同じRaceEntryから旧Horse参照と新identityが同時に得られた場合に限定する。名称一致だけでは実行しない。
3. canonical Horseは、既に当該JRA identity mappingを持つHorseを優先する。mappingが未確定な既存重複だけなら、参照数が多いID、同数なら作成時刻が古いIDを選び、結果を監査へ記録する。
4. 統合元Horse IDは削除せずredirect/tombstoneとして保持する。これにより外部リンク、過去イベント、保存済みjob属性からの参照を回復可能にする。
5. `accessD` / `accessS` の開催選択ページはDiscovery起点として許可するが、RaceCard/RaceResult子requestのExplicit URLには単一・非空の `CNAME` とResource整合性を必須とする。
6. URL不正時に推測でパラメーターを組み立てない。一覧上の検証済み詳細linkを使い、得られなければ通常Navigator Discoveryへフォールバックする。

## Acceptance criteria

| ID | Observable criterion | State |
|---|---|---|
| AC1 | RaceCardの馬名セルにJRAプロフィールlinkがあると、Horse resolve、RaceEntry保存、horse-profile requestのすべてに同じJRA identityが伝播する。 | Not started |
| AC2 | 同名で異なるJRA identityの競走馬は別Horseとして保存され、名称Discoveryで相互に誤選択されない。 | Not started |
| AC3 | 同一JRA identityを2Workerが同時にresolveしても、1つのcanonical Horse IDだけが返り、mappingとHorseが重複しない。 | Not started |
| AC4 | 同一JRA identityが複数内部Horseへ結び付いたことを検出すると、RaceEntry等の参照がcanonical Horseへ移り、source IDはredirectされ、監査記録が1件だけ残る。 | Not started |
| AC5 | 名前だけが一致しidentity/provenanceが不足する候補は自動名寄せされず、要対応として確認できる。 | Not started |
| AC6 | `accessD.html` / `accessS.html` のパラメーターなしURLはRaceCard/RaceResult子requestのExplicit URLとLocationに保存されない。 | Not started |
| AC7 | 単一・非空の `CNAME` を持ち対象Resourceと整合する詳細URLは保存され、direct取得に成功する。不正URLは通常Discoveryへフォールバックする。 | Not started |
| AC8 | 現在のビッグヒーロー対象は、関連RaceCardの馬linkから2023年登録のJRA identityへ一意に関連付けられ、profile取得が成功する。異なる同名2頭は統合されない。 | Not started |
| AC9 | 既存データをdry-run監査し、同一JRA identityの重複、名称だけ一致する候補、bare accessD/accessS Locationの件数を記録してから、安全対象だけを移行する。 | Not started |
| AC10 | parser、API同時実行、merge transaction、redirect、Discovery→child request、Worker direct/fallbackの統合テストが成功する。 | Not started |

## Delivery plan

1. RaceCard semantic fixtureと実サイトsnapshotでHorse profile linkの所在・URL構造を固定し、parser/modelへidentityを通す。
2. Horse external identity mapping、merge redirect、merge auditを追加し、atomic resolve-or-registerと冪等mergeを実装する。
3. RaceCard保存経路をcanonical Horse IDへ接続し、profile requestへidentity/Locationを渡す。
4. RaceCard/RaceResult詳細URLvalidatorをDiscovery、Location登録、direct handler境界へ接続する。
5. dry-run移行で重複候補を分類し、強い証拠がある対象だけ名寄せする。ビッグヒーローは関連RaceCard linkでidentityを確定する。
6. end-to-end、競合、再実行、partial failure/restart、誤統合防止を検証し、本番再取得結果を記録する。

## Verification record

- 2026-09-13: 本番対象画面で内部Horse ID `horse-349b8cd2-8278-5267-a7d7-2b7ab5f011bb`、名称「ビッグヒーロー」、生年月日なし、Location 0件を確認した。
- 2026-09-13: 同画面の診断で `SubjectIdentification:MultipleCandidates` と、異なるJRA `accessU.html?CNAME=...` を持つ同名候補3件を確認した。
- 2026-09-13: 本番Horse一覧では「ビッグヒーロー」の内部レコードは現時点で1件だった。JRA候補3件は同名別馬であり、その3件自体を名寄せしてはならない。
- 2026-09-13: 現行RaceCard parserの `RaceEntry` はHorse profile URLを保持せず、RaceCard handlerは馬名だけからHorse IDとprofile requestを生成することを確認した。
- 2026-09-13: 現行Horse Upsertはクライアント側で名称由来IDを生成し `GET -> POST/PUT` を行う。作成競合自体はConflict後Updateへ収束するが、外部identityによる同名別馬識別と表記揺れ重複の収束は保証しないことを確認した。
- 2026-09-13: Race Discoveryは一覧parserのURLを `CollectionHttpUrl.Resolve` した結果をそのまま子requestへ渡し、JRA詳細パラメーターの必須検証を行わないことを確認した。

## Deviations and follow-up

- ユーザー承認前のため、プロダクションコード・DB・本番データは変更していない。
