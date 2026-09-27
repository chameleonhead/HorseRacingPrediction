# DOMに基づく主体名取得と現在の収集エラー対応

- Status: Approved
- Change record schema: 2
- Owner: Main / collection maintainer
- Created: 2026-09-27
- Updated: 2026-09-27

## Completion summary

承認記録: 利用者が再確認間隔を5分へ指定した後、「そのほかは問題ないです」と明示。AC1–7とC1–6（血統8件保留、本番配備/補正/再開の追加承認を含む）を承認としてExecution Modeへ移行する。本番操作は追加承認待ちであり、設計の承認と本番操作の承認を区別する。

| Dimension | State | Evidence or remaining work |
| --- | --- | --- |
| Code | Verified | f32e2f41、DOM名/保存/有限待機/identity/revisionの実装とローカル受入を確認 |
| Verification | In progress | local 1413 passed / 1 existing skip。Linux CIは関連PR checksで確認 |
| Deployment/operation | Not started | 本番はGETのみ。補正・再開は対象差分提示後の追加承認が必要 |

## Context / goals

利用者は、騎手名を文字列の後処理ではなくDOM境界から正しく取得するよう再設計し、現在の収集エラーにも対応することを要求した。
収集停止の解除、名前だけによる主体統合、既存データの削除を今回の調査から推定して実施しない。

11:40 JSTの本番は停止中。現在の停止原因は阪神4Rの `JraResultConsistencyException` であり、過去の引退騎手リンクエラーではない。
actionable failureはRace 1件、Jockey 1件、Horse 14件。Horseグループの代表メッセージだけで14件全部をNoCandidateとは扱わない。
個別内訳は、公式URL付きの現役馬5件（同名候補4件・候補なし1件）、ディープインパクトの名前不一致1件、名前だけの血統上の馬8件（候補なし6件・複数候補2件）。

中山11Rの16頭中15頭は、現在の公式出馬表を現行parserへ通すとレーティングが騎手名へ混入する。本番の同レースにも同じ15件の混入を確認。
新しい `106 M` 除去正規表現や数値を削る共通normalizerは採用しない。

## Evidence / hypothesis ledger

再現コードは[読み取りprobe](probe/Program.cs)、観測結果は[調査証拠](evidence/read-only-diagnosis.json)。
調査元HEADは1d3c953。probeは公式サイトへの読取とローカルChromium内の故障注入のみ。APIキー・headers・認証情報は保存しない。

| ID | Claim / fact boundary | Supporting / contradicting evidence | Falsification and result | Disposition |
| --- | --- | --- | --- | --- |
| H1 | 出馬表の結合セルが騎手名汚染の原因 | 実DOMはtd.jockey内のp.jockeyと兄弟div.ratingに分離。現在のExtractJockeyNameはkg以降全体を採用 | URL→PlaywrightPageSnapshotter→RaceCardPageParserを実行し15/16件で再現。p.jockeyの16件は正しい名前のみ | Confirmed、AC1 |
| H2 | a.jockeyを選べばよい | 合成fixtureにはa.jockeyがあるが、実DOMのaはclassなし、親pにjockey。hrefは#、onclickのdoActionで遷移 | 実HTMLとbrowser DOMを照合。既存parserのProfileUrlは全16件null | a.jockey案を却下。名前にp.jockeyを使う。URL生成はしない |
| H3 | 引退一覧の公開リンクが消えた | navigationは「引退騎手一覧」を完全一致検索 | live directoryの実リンクは「引退騎手」→jretirement.html、「引退調教師」→retirement.html | 公開リンクは存在。ラベル契約の誤り、AC2 |
| H4 | 阪神4Rの公開途中が停止原因 | 11:28の例外は完走馬3のTime欠損。11:45以降の同じURLは全7頭のTimeあり、馬3は2:01.2 | 現在DOM→既存parserは成功。ローカルDOMの当該Timeセルだけを空にしてsnapshot→parserを実行すると同一例外。発生時DOMは未保存 | 欠損→停止経路は再現済み。欠損が起きた歴史的原因は未確定。公開途中と断定せず、限定再試行と証拠追加、AC3 |
| H5 | 公式Horse identityがfallback中に失われる | handlerはReferenceRaceなしならSourceIdentityをnull化。現役馬5件の保存metadataに公式sourceIdentityがあるがReferenceRaceがない | 既存ProfileLocationFailure_FallsBackByNameWithoutFailedSourceIdentityテスト1件成功。現行動作としてnull化を確認。ハクタカ・ヴェントリナの公式URLを今読むとprofile parse成功 | null化機構はConfirmed。初回URL遷移の失敗理由は未保存で未確定。安全なidentity保持、AC4 |
| H6 | ディープインパクトは別馬なので不一致 | h1のhorse_icon画像altが「マルイチ」、txtの直接テキストは「ディープインパクト」、name_enは別span | 実URL→snapshot→parserで「マルイチ ディープインパクト」を再現。DOMの直接テキストには画像altが混じらない | DOM名抽出の欠陥。検証を弱めず画像を名前に含めない、AC4 |
| H7 | 残る血統馬8件も自動復旧できる | metadataに名前/発見元/depthだけ、sourceIdentity/生年月日なし。6件候補なし、2件複数候補 | 本番14件全件のresourceを読取。8件には一意同定を保証する保存根拠がない。現在の公開検索全件の再探索は未実施 | 自動同定可能とは主張しない。元情報/失敗を保持し保留。AC5 |

調査の証拠限界：現在のHTMLは失敗時のHTMLではない。HTTP GET成功や今のparser成功を本番worker成功と取り違えない。

## Decisions / technical impact

### 1. DOMによる騎手名

- 対象の出走表・対象行・騎手セルまで限定し、その中の一意な `p.jockey` fragmentのTextを使う。過去成績セル内の `div.jockey`、兄弟のrating/斤量/性齢は取得元にしない。
- 独立した「騎手」「騎手名」列では、その専用セルまたは名前要素を使う。結合列に専用要素がない、複数ある、snapshotが切れている場合は構造化エラーにし、セル末尾へfallbackしない。
- 空白・既存の減量記号正規化だけを選択済みの名前へ適用する。英字・外国人名・句読点を削らない。名前未公表/取消等の正当な欠損と構造異常をfixtureで区別し、根拠がない表記を正常扱いしない。
- 通常保存とrefresh/bulkを検証する。現行通常保存は既存entryのJockeyIdを常に維持するため、正しいincoming名に対応する主体を解決して騎手情報を更新できるようにする。既存の誤ったIDのmasterを別名へ上書きしない。
- Race/Horse/Entry ID、馬の紐付け、結果、予想、取消行は保持する。馬番・枠番は識別キーへ戻さない。
- `href=#` やonclick引数からプロフィールURLを推測生成しない。専用DOM名で現行の公開ディレクトリ探索を利用する。POST導線の新しい汎用モデルは本件の必須変更ではない。

### 2. 公開ディレクトリ

実DOMで確認した「引退騎手」「引退調教師」の公開anchorを選ぶ。既存のHTTPS/JRA host検証、リンクの一意性検証を維持する。
不正host、複数遷移先、リンク欠落は成功/対象外へ丸めない。今回の汚染名をそのまま検索してNotApplicableにするだけの対応はしない。

### 3. 直後の結果のTime欠損

保存前の結果検証は維持する。特定の構造化理由「完走着順があり、存在するTime列のセルだけが空」を判別し、
同じRace identity、当日JST、確認済み公式発走時刻から30分以内の場合だけ、5分後に再確認する。
待機は未完了であり、部分Result保存・成功扱い・URLの不良確定をしない。期限は元の公式発走時刻基準で、再試行や新task作成で延長しない。
30分を超えた欠損、発走時刻の根拠なし、異なるRace、Time見出し欠落、不正な非空Time、その他の整合性異常は従来の異常扱いを維持する。
直接Result locationとCard経由fallbackの両方へ接続する。専用reason、Race/馬番、見出し、対象行の安全なセル値、URL、観測時刻、公式発走時刻、期限を記録し、次回は失敗根拠を追えるようにする。
raw HTML全量や認証情報は運用ログへ入れない。

これは「当時は必ず公開途中だった」という仮定ではなく、短期的に不完全な入力を保存せず再確認する有限の方針である。
未知の障害を一律に隔離したり、自動停止を無効化したりしない。

### 4. Horse profile

- レースの公式Horseリンクから得た、検証済みsourceIdentityはURL遷移失敗後も保持する。公式JRA accessU/CNAMEの同値規則を使い、異なる同名馬へfallbackしない。
- 任意の古いlocationを新しいidentityの根拠にはしない。保存されたsourceIdentity/sourceUrl/発見元Raceの一致を検証し、競合や不一致は保留する。完全一致がなければ名前だけで置換しない。
- profile見出しをflattenして画像altや英語名を切る方式から、意味のあるDOMテキストへ変更する。generic snapshotのsource情報へ要素自身のClassTokensを追加し、Heading内の `span.txt` に属する直接Textを選ぶ。Image、opt、name_enは名前の取得元ではない。
- 馬の名前・公式identity・生年月日の保存時検証を維持。共通normalizerへ「マルイチを削る」という新しい例外を追加しない。
- 名前だけの血統上の8件は未同定のまま隔離/保留を維持する。データ欠損を隠す成功化、曖昧候補の選択、全failure消去はしない。追加根拠が得られた場合だけ別途一意照合する。

### 5. 取得revisionと既存データ

共通descriptorのRaceDetail 5→6、HorseProfile 4→5、JockeyProfile 3→4、TrainerProfile 3→4へ変更する。
API/Collector/initializer/手動要求/refresh/復旧経路を同じ版へ接続し、既存Currentも新しい抽出で再取得対象になることを検証する。
版を上げただけで過去の誤要求が解決するとは扱わない。

補正previewは、旧task/request・既存Race/公式Horse identity・新DOMでの一意な名前を照合し、旧/新騎手ID、影響entry、関連job、未同定理由を出す。
文字列置換で旧master全体を改名・統合しない。最低限、中山11Rの既知15件を追跡し、別レースへの拡大はpreviewで明示する。
本番データ補正、旧誤要求の抑止、再要求、停止解除は、実装後に差分を提示して追加承認を得る。承認前にrevision上昇版を稼働中のcollectorへ配備しない。

## Non-goals / authority boundary

- UIのリソース名表示変更、全主体IDの再設計、別データ提供者の導入、血統馬の推測同定は含めない。
- 出馬表を削除しない。取消・除外馬は出馬表に保持し予想対象外という既存契約を維持する。
- 本番操作の承認までは、今回の承認依頼はプログラム修正、ローカル/CI検証、read-only補正preview作成の範囲。
- 本番対応そのものは要求の未完了項目としてT6に保持する。後続承認が必要なことを理由に「復旧完了」としない。

## Concern and agreement ledger

| ID | Concern and evidence | Impact / alternative | Proposed disposition / residual risk | AC/task/test | Agent position | User disposition | State |
| --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | 合成fixtureのa.jockeyと実DOMが違う。URLはPOST導線 | aのclass前提/推測URLは再発する | p.jockeyを使う。URLモデル拡張は不要。DOM変化は明示エラーと実DOMfixtureで検出 | AC1/T1、欠落/複数/過去成績 | 推奨 | 承認済み | Resolved in design |
| C2 | 失敗時の阪神4R DOMがない | 公開途中と断定不能。無限retryは異常を隠す | 30分/5分の有限再確認、部分保存禁止、期限後の異常と安全な証拠保存。短時間の真の構造異常の検知が遅れる残存riskは期限で制限 | AC3/T3、期限/日跨ぎ/非空不正値 | 推奨 | 5分間隔は利用者指定、残る設計も承認済み | Resolved in design |
| C3 | 正しい名前を読み直しても既存JockeyId保持経路がある | parserだけでは既存汚染が残る。全master改名は参照破壊 | 保存経路も修正し、補正は根拠付きpreview後の別承認。旧ID/イベントは削除しない | AC1,6/T1,T5,T6、再取得/古いworker | 推奨 | 承認済み | Resolved in design |
| C4 | sourceIdentity放棄で別の同名馬へ進み得る | stale location救済と主体同定を混同 | 公式source evidenceと単なるlocationを分け、矛盾時保留。候補なしの自動解決は保証しない | AC4/T4、別CNAME/不正host/誕生日 | 推奨 | 承認済み | Resolved in design |
| C5 | 血統馬8件は同定根拠不足 | 「全エラーゼロ」は別馬選択か失敗隠蔽になる | 当面保留して名前/血統関係を保持。8件の自動同定は除外を提案。Mainが一覧と追加根拠の要件を保持し、新根拠取得時に再検討 | AC5/T4,T6、候補0/複数 | 推奨 | 8件保留を含め承認済み | Resolved in design |
| C6 | revision増加/再開は既存データを更新する | 配備だけでも再収集が始まり得る | ローカル/CI/previewを先行。本番のpause/drain、同版配備、allowlist、冪等性、履歴保全、再開の対象を次段階で承認 | AC6,7/T5,T6 | 推奨 | 承認済み | Resolved in design |

安全・データ・検証盲点・運用・権限・互換・費用の観点を確認。公開途中説など未確定の歴史的原因は実装前提に使わない。
提案中のリスク境界/除外に利用者が同意しなければ、承認扱いにせず設計を更新する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 出馬表の16頭からDOMの騎手名を取得しrating/斤量/過去騎手を混入させない。通常保存/refresh/再取得も同じ正しい騎手へ接続。欠落/複数要素は誤名保存せず、馬/Entry/取消行/番号未確定の既存契約を維持 | T1,T5 | live由来HTML→snapshot→parser、HTTP保存/read-back、既存誤JockeyIdの再取得反例 | Verified |
| AC2 | 実際の引退騎手/調教師リンクを辿れる。不正host/曖昧/欠落は成功化せず、汚染名の対象外処理だけで復旧としない | T2,T5 | 公開anchor fixture→navigation→handler、正常/拒否のテスト | Verified |
| AC3 | 発走直後の限定的なTime空欄は5分間隔・公式発走から30分以内の有限待機とし、完全な再取得後だけ保存。期限・根拠・別Race・列欠落・不正値の条件外は異常を維持。直接/fallback両経路の証拠とRetryAtを保存/read-back | T3,T3-tests,T5 | 実DOM故障注入、clock制御で5分後のRetryAt、部分保存0、期限後異常、worker/store統合 | Verified |
| AC4 | Horseプロフィールの登録画像alt/英語名をDOMで除外し正しい馬名を取得。信頼できる公式identityをfallbackでも維持し、同名別馬・誕生日/URL矛盾を保存しない | T4,T5 | Deep Impact実DOM、明示identity→失敗→同名候補、候補なし/異CNAME/誕生日の反例 | Verified |
| AC5 | Horse14件を個別に分類し、修正で再取得可能な対象と、根拠不足の血統馬8件を区別。8件は名前/血統情報/失敗を残して保留し、自動同定・成功化しない | T4,T6 | 全件台帳、metadata/URL/名前の照合、保留8件の履歴保持 | Verified |
| AC6 | revisionを共通定義で進め、補正previewで旧/新ID・対象・影響・除外を確認できる。本番補正/配備/再開は追加承認まで行わず、データ削除や予想/結果の付替えをしない | T5,T6 | 全entry point棚卸し、revision再取得テスト、preview前後DB不変・機密検査 | Verified |
| AC7 | ローカル/CIの関連・全体回帰と統合検証を完了。次段階承認後はGitHub経由の同版配備、対象限定復旧、正常後続batchまたは15分の進捗を確認し、先行馬主46件の未完了AC6にも証拠を還元 | T5,T6 | CI、配備SHA、対象成功/履歴保持、停止再発なし。承認待ちは未完了として記録 | Connected |

## Task plan / routing

Execution checkpoint: T1 DOM実装とT2を含むScraping nonExternal 351件成功。T3の有限待機実装はCollector build成功。T4はMainが開始する。T3の設計判断はMainが保持し、凍結済み条件の反例テストだけをT3-tests（cost-sensitive worker / requested gpt-5.6-luna、audit T3-tests-A1）へ分離する。write scopeはCollector/Scrapingの各新規JraIncompleteResultTests.csだけ。AC3、依存T3 build済み、state Runnable。workerはfocused parser/handler testsを実行、Mainはstore統合・全体回帰・受入を担当する。

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State | Routing | Audit | Result metrics |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| D1 | DOM/caller調査 | dom_jockey_design_inventory | Balanced explorer | - | read-only | Main実DOM反証、source照合 | p.jockey / POST / source class不足を確認 | Verified | requested gpt-6-sol、範囲限定探索 | no coding | model/usage未確認; retries0; review1 |
| D2 | Time例外/再試行調査 | race_time_failure_design | Balanced explorer | - | read-only | Main live+故障注入 | 同一例外、保存前の停止、公式時刻の経路 | Verified | requested gpt-6-sol、独立探索 | no coding | model/usage未確認; retries0; review1 |
| D3 | Horse failure経路調査 | horse_failures_design | Balanced explorer | - | read-only | Main全14件/既存test/実profile | identity放棄、画像alt。祖先候補なし件数はMainが6件へ訂正 | Verified | requested gpt-6-sol、独立探索 | no coding | model/usage未確認; retries0; corrections1; review1 |
| T1 | 騎手DOMと通常/refresh保存接続 | Main | Lead | 承認 | RaceCard/Result parsing, Collector Http writer、関連tests | AC1 | DOM実ページ16頭、HTTP再取得test成功 | Verified | 既存参照/保存契約の判断と接続をLead保持 | none | lead verification; usage unavailable |
| T2 | 引退リンク選択 | retired_link_worker | Cost efficient coding | 承認 | JraNavigator.Subjects.csの騎手/調教師部分、専用新規test、既存fixture2箇所 | AC2、focused→Scraping回帰 | focused8/既存含むScraping351成功 | Verified | 凍結した選択規則のみ。Horse navigator変更と直列化 | T2-A1 | usage unavailable; retries1 (regression handoff); corrections0; reviews1 |
| T3 | Time欠損の有限再確認/証拠 | Main | Lead | 承認 | Result parser例外、race handler、completion DTO/store、関連tests | AC3 | parser3/handler7、Main worker→store+日跨ぎ成功 | Verified | 時刻/並行性/停止境界/transportは分離せずLead | none | lead verification; usage unavailable |
| T4 | Horse DOMとidentity保持 | Main | Lead | 承認,T2 | Snapshotモデル/source、SubjectProfile parser、subject handler、関連tests | AC4,5 | 実DOM Deep Impact、DOM5例/identity反例6件、subject+HTTP49成功 | Verified | generic source契約と既存同定互換をLead保持 | none | lead verification; usage unavailable |
| T5 | revision/統合/回帰/文書 | Main | Lead | T1–T4 | Contracts revision、integration tests、docs | AC1–4,6,7 | Release build/全体1413成功。CIは関連PR checks参照 | In progress | shared出力/全体統合/最終判断 | none | 未実施 |
| T6 | preview/対象確認/配備復旧 | Main | Lead | T5、本番操作は追加承認 | preview tooling/docs、承認後GitHub/限定運用 | AC5–7 | preview16件同定/15件変更。配備承認待ちは後段 | In progress | 機密/データ補正/権限と最終受入 | none | 未実施 |
| T3-tests | 凍結した有限待機の反例tests | incomplete_result_tests | Cost efficient coding | T3 build | Collector/Scraping新規JraIncompleteResultTests.cs | parser63+handler7+existing40成功 | Main独立store/日跨ぎ追加確認 | Verified | テスト作成のみを分離、設計はMain | T3-tests-A1 | usage unavailable; retries0; corrections0; reviews1 |

判断と機械的実装の分離を検討済み。T2は実DOMのラベル/安全性が確定した局所変更としてlow_cost_coding_worker候補。
T2の最小テストは新規引退リンクテスト、handoff前はScraping全nonExternal。欠落/複数URL/悪性host/同一URL重複を含む。
workerは他作業を取り消さず、公開契約/identity/保存変更へ広がる場合はMainへ戻す。T4と同じnavigatorファイルの並列編集は禁止。
残るLead streamは公開契約、参照/停止/時刻の整合、統合の判断を含む。凍結後に独立fixture作成を分離できる場合だけ再分割し、ownerと監査を先に記録する。
探索モデルはrequestedのみ確認可能。observed model/token/費用は未確認、料金や優劣を推測しない。コーディング委譲前にaudit artifactとvalidatorを適用する。

## Delivery / operation

1. この提案のAC/懸念/保留対象について明示承認を得る。
2. 実装、DOM由来fixture・HTTP/worker/store統合、format/build/全体nonExternal、既存ローカルhost検証、Linux CIを実施。
3. 本番GETで最新状態と対象を再確認し、補正preview・実行順序・backup/pause/drain/rollback条件を提示。本番補正/再開の追加承認を得るまで止める。
4. 承認後、GitHub push/PR mergeの既存配備経路だけを使いAPI/collector同版にする。未確認の手動AWS配備は行わない。
5. 確認済み対象だけ再取得/補正。阪神4Rには11:29登録のReady taskがあるため重複登録しない。旧failure/attemptを消さない。未解明の新しいsystemic failureは対象へ勝手に追加しない。
6. 元の対象と後続進捗を観測。本番workerの成功まで完了扱いしない。PR作成前に当該目的のcommitを完成し、merge後のbranchへ追加commitしない。merged branchはlocal/remote削除。

本番修復コードや最終allowlistはpreviewで判明する参照影響に依存する。今回の承認で破壊的操作や未提示のmaster統合まで許可されたとは扱わない。

## Documentation updates

- JRA site contract impact: Updated
- `docs/27-jra-site-collection-contract.md`: 実DOMのp.jockey/rating、POST導線、horse_icon/name_en、引退リンクを確認事実として追記。提案と実装済みを分離する取得元正本。
- `docs/26-collection-platform-design.md`: 有限Time再確認、identity保持、revision/preview/操作承認の提案リンク。収集運用の正本。
- `docs/10-domain-design.md`: 騎手参照の再取得と旧master/馬Entry/履歴保全の提案境界。domain正本。
- 先行 `20260927_shared-identity-contracts` のAC6は未完了を維持し、最新の別件停止と46件Ready/attempt0の読取結果を追記。子recordを作成して元の受入を除外しない。

## Review gates / verification / next action

設計調整（2026-09-27）: 利用者の「発走直後のタイム取得は5分間隔程度で問題ない」に従い、再確認間隔を2分から5分へ変更し、AC3と収集設計正本へ反映した。30分の期限、部分保存禁止、本番操作の別承認は提案のまま維持する。この間隔指定だけで設計全体をApprovedとは扱わない。今回は文書だけの短い修正のためMain単独で対応する。

Design/task-split: Mainが探索結果を実DOMで反証。a.jockey前提を却下しp.jockeyへ修正。全Horse件数の誤集計もAPI個別記録で修正。
AC1–7はhappy path/誤取得/不変条件/統合と運用境界を対応付けた。プロダクション編集は承認前のため行わない。
Concern/agreement: C1–6の推奨処置を提示する。元のTime欠損原因は不明のまま明示し、有限待機をその推定に依存させない。血統8件保留と補正別承認も承認依頼本文へ含める。
Pre-implementation: Mainが2026-09-27に承認・境界・AC対応を確認。T1/T2/T3はRunnable、T4はT2とnavigator共有のためDependent、T5/T6は前段検証待ちDependent。本番operationは追加承認まで実行不可。T2はgpt-5.6-lunaの局所workerがSubjects.csの引退リンクと専用testのみ所有し、Mainはそれらを並列変更しない。T1はRaceCard parser/通常HTTP保存、T3はResult例外/handler/store、T4はsnapshot/馬profileをMain所有。ビルド出力は共有のためテスト実行を調整する。Checkpoint / Final reviewは未実施。

検証契約: T1はRaceCard parser focused→Scraping回帰とHttpDataCollectionWriteServiceの再取得HTTP反例。T2は引退リンク専用test→Scraping nonExternal全件。T3はTime空欄parser、race handlerの5分/30分/日跨ぎ/別Race、completion store read-back。T4は実DOM profile+identity fallbackのfocused→Scraping/Collector回帰。T5はCIのexact format/Release build/full nonExternal、DB/localhost gate。すべて成功をhandoff条件とする。T2で安全URL規則の変更や主体識別への拡大が必要ならMainへ戻す。

- `dotnet run --project docs/changes/20260927_dom-jockey-and-collection-errors/probe/Probe.csproj -- <公式URL...>`: 出馬表15件の汚染、Deep Impactの画像alt混入、両引退リンクを確認。阪神4R正常7件と局所Time空欄の同一例外を確認。
- `dotnet test tests/HorseRacingPrediction.Collector.Tests/HorseRacingPrediction.Collector.Tests.csproj --no-restore --filter "FullyQualifiedName~ProfileLocationFailure_FallsBackByNameWithoutFailedSourceIdentity" --verbosity minimal`: 1成功。既存のSourceIdentity放棄動作の確認であり修正検証ではない。
- CodeGraphは既存stubのみでindex未初期化。`codegraph explore`不可を確認済み。勝手にindexせずsource探索と実経路probeを使用。
- 11:50頃の既存Owner46対象GETは全件Ready、attempt0。収集復旧も先行AC6も未達。
- 最終文書確認: `python .codex/skills/document-driven-development/scripts/validate_change_records.py <本record> <先行record>` はissues=0、`git diff --check`成功。最初のroot scripts参照は存在せず、skill配下の実スクリプトへ訂正した。
- 調査probeの最終 `dotnet build .../probe/Probe.csproj --no-restore --verbosity quiet` は警告0/エラー0。これは設計用ツールの検証であり、未実装の修正コードや本番復旧の完了証拠ではない。

### Execution checkpoint (2026-09-27 13:00 JST)

- T1–T4の実装/関連検証が成立。revision Race6/Horse5/Jockey4/Trainer4、Owner1は変更なし。
- 実DOM再取得: 中山11R16頭の騎手名とDeep Impactの馬名が正しい。[修正後probe](evidence/fixed-live-probe.json)。
- [GET-only補正preview](evidence/correction-preview.json): 保存taskのsourceIdentity/sourceUrl/発見元Raceと公式Horse IDを16/16照合。15騎手参照変更、1不変。前後のRace JSON一致。馬番でjoinせずHorse IDでjoin。全レース走査ではない。
- worker→HTTP serialization→store統合で欠損理由/馬番/時刻/期限/5分retry read-back成功。公式時刻はrequest metadataの任意値ではなく既存RaceEvidenceからleaseへ補完される。
- process defect: compact監査schema2が既存validatorに未対応だった。learn-from-implementation-failures/skill-creatorに従いvalidatorへcompact分岐と4反例testだけ追加（routing方針変更なし）。legacy込み18test、UTF-8 quick_validate、実T2/T3-tests監査をforward testとして成功。次のcompact auditでも観測し、legacy gate回帰なら変更を戻して修正する。料金/実model/usageは推測しない。
- 検証失敗/closure: (1) snapshotへ空ClassTokens配列を常時出しcompact-size反例が失敗。null省略へ変更し元のサイズtest+DOM5件成功。(2) Main store統合fixtureに予約metadata officialStartAtを直指定して拒否された。実completionでRaceEvidenceを保存する正しいfixtureへ修正、元test成功。(3) Main日跨ぎfixtureのcourse/number欠落を修正、元test成功。(4) worker編集中のtest build失敗は所有者へ通知し、handoff後の回帰成功を採用。
- 次操作: full Release regression再実行→format verify/DB/localhost→実装/プロセス修正/文書を目的別commit→PRとLinux CI→本番対象allowlist/復旧手順の追加承認依頼。未コミットはgit statusにある当該source/tests/docs/監査validatorのみ。無関係な変更・本番mutationなし。

追加検証: API bulk再取得→GET read-backで、旧汚染騎手から正常騎手へ参照変更しHorse/Entry IDと旧master名を維持するテスト成功。DB pending modelなし・empty SQLite migrations成功（既存EF tool8/runtime10のwarningあり、変更対象外）。localhostの隔離API2process検証は14entries/14owners、read-only preview/cross-process lock/idempotency/durable hold/backup/delayed odds rejection成功。本番接続なし。配備ガード14ケース成功。既存ブラウザー取消待機テストは全体並列実行時2秒制限に2.775秒で抵触したため、閾値を変えずScraping全360件を単独再実行し成功。全solution直列回帰は1413成功・既存skip1・失敗0。CodeGraph syncは未初期化stubのため不可、index新設なし。

本番の次段階は[段階別対応案](production-plan.md)。現段階の最終報告は本番復旧完了を意味しない。CIの実行結果は関連PRのchecks/検証本文を証拠とし、PR作成後の追加commitで結果だけを追記しない。

Final local review: [集計](evidence/local-tests.json)はRelease full nonExternalのexit0をTRXから再集計した。AC1–6は各task/実DOM/HTTP再取得/worker-store/previewに追跡可能。AC7の本番進捗と先行Owner46のAC6は未達のまま。process修正commit50643536とproduction修正commitf32e2f41を分離。残るローカル変更は本record・取得元/設計文書・読取probe/証拠のみ。CI失敗なら実行ログを保持して対処し、本番へ進めない。CI成功後の次の外部権限ゲートはproduction-plan.mdの段階1であり、本recordはApprovedを維持する。
