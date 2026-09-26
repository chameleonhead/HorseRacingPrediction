# Identity entry-point and remaining-symbol inventory

Mainによるsource/literal棚卸し。CodeGraphは未作成stubのため、graph-basedな完了証拠にはしない。最終diffと全体gateが通るまでは本表だけでVerifiedとしない。

| Surface | Classification / shared boundary | Evidence / remaining check |
| --- | --- | --- |
| API bulk / refresh / Owner API / Owner merge aliases | active: `OwnerIdentityContract.ResolveId` + DB mappings | SharedOwnerIdentityTests: canonical法人・幅・英字、統合alias、worker存在確認。歴史名とaliasは保持 |
| API horse preflight / direct HTTP writer / profile-derived jobs | active: `CollectionIdentityResolver` + `BuildHorseId` | 一括snapshotを各validation passで再読取。旧ID保持、同名複数・公式identity矛盾・名前だけの公式馬結合を拒否 |
| Subject identification pre-dispatch repair | active: 同じDB resolver。revisionも共通契約 | 元Race参照を追加条件とし、曖昧な候補はnull/保留。source不正をfallbackで隠さない |
| Horse identity repair / entry repair manifest | active, intentional evidence validation | 公式sourceに対する決定論的ID比較。新規IDを無条件に上書きする経路ではない。既存のpreview/hold/backup/apply条件を維持 |
| `BuildSubjectProfileMigrationPreviewAsync` / `ExpectedSubjectId` | migration-only, currently unregistered private helpers | 宣言とprivate内呼出のみ。通常API/runtime登録から呼ばれない。過去migrationのhash照合はruntime resolverの代用にしない |
| Navigator / profile parser / profile API | active: `NormalizeIdentityName("Horse", ...)` / `JraSourceIdentity.MatchesHorse` | 実検索候補の固定snapshotとHTTP保存更新を検証。CNAMEの違い、誕生日、不正hostを拒否 |
| API race identity / history prepare / bulk / direct writer / result workflow citation | active: `CollectionIdentityResolver.RaceAsync` | 日付・canonical場・番号が一意なら既存ID。結果workflowは実APIの解決IDを引用元と戻り値に使用 |
| Race write lock / active lease / repair hold / worker acquire | active: persisted ID resolution; `IRaceResourceIdentityResolver` API adapter | 任意の旧RaceIdと英日表記、contradictory metadata、hold中のlease拒否を統合test |
| Collection store without domain provider | intentional history compatibility | standalone initializer/store testsではcanonical-only conservative判定。API本番・TestApplicationFactoryはdomain adapterを登録。未解決aliasはholdを回避しない |
| `IDataCollectionWriteService` default race resolver | intentional non-persistent adapter fallback | 本番唯一の実装HttpDataCollectionWriteServiceはAPI resolverへoverride。defaultは既存test doubles向けで、production Collectorに独自hash生成を残さない |
| Course name / resource code / initializer / legacy merge | active: `RaceCourseIdentity` | 10場英日・case-insensitive。Scrapingの本文中の部分抽出とJRA数値場コードは別の入力契約であり、永続ID生成ではない |
| API Program / Collector handler registry / CollectionInitializer registration | active: `SubjectCollectionDefinitions` | 主体type/definition/revision/persistenceを共通記述。initializerにもOwner handler定義を含む |
| Bulk/discovery/manual recovery producers | active: descriptor or current registered revision | 手動APIはstoreに登録されたcurrent revisionを参照。共有定数だけで全件再収集はしない |
| DomainCollectionSeedReader profile seed revision 1 | intentional history compatibility | 既存profileの取得内容が新revision相当だったと偽らない。登録definitionはcurrent、既存観測のapplied revisionは歴史証拠どおり |
| Api appsettings per-definition numeric values | active, unrelated concurrency limit | revisionではなくworker batch/concurrency設定。置換しない |
| Entry ID / assignment fingerprint / repair hash / batch key | active, distinct purposes | 対象外のhash。既存EntryIdは再生成せず保持。予想/結果付替え・event再採番なし |
| New owner repair preview / execute | active, exact selection only | 誤IDが別の有効Ownerでないこと、元task name/race、現在alias・Owner存在を確認。snapshot fingerprint照合、pause/drain、冪等request→旧resource抑止、失敗attempt保持 |
| Owner repair former `BuildEntityId("owner", NormalizeKey(name))` | migration-only evidence | 元taskの要求名から旧不具合IDを正確に再現できることを必須化。任意の不存在IDは復旧対象にしない |
| Initial HorseRegistered event lookup | intentional history compatibility, read-only | 表示名補正前の名前由来IDを初回eventで証明。同値名のみ許容し、event書換え・公式馬の名前だけの結合はしない |
| Identity write / owner repair / pipeline resume lock | active, shared cross-process coordinator | alias変更と復旧再検証を直列化し、復旧中のresume割込みを防止。停止操作は妨げない |

## Verification commands

- `rg -n 'BuildHorseId|BuildRaceId|BuildEntityId\("owner"|ExpectedSubjectId|NormalizeIdentityName' src tools -g '*.cs'`
- `rg -n 'horse-profile|jockey-profile|trainer-profile|owner-identity' src tools scripts -g '*.cs' -g '*.json' -g '*.ps1'`
- `rg -n 'SetPausedAsync\(false|IRaceResourceIdentityResolver|RegisterDefinitionsAsync' src tools tests -g '*.cs'`

## Local verification corrections

- 最初のbulk計測は18頭でsubject照会57回となり不合格。identity snapshotを一括取得へ修正。subject set query 3回に、lock前後/preflightの安全な再照合3回を加え、計6回（頭数に非依存）・書込transaction 1回をassertする。旧3回という実装値だけを保持するために安全照合を省略しない。
- 厳密なURL host/scheme確認によりabout:blank上の相対linkは拒否される。ローカルHTMLtestのpage originを実際のJRA originに合わせ、productionのhost制限を緩和しない。protocol-relative外部host、file/about schemeの反例を追加。
- 全体再検証で3件のfixture差異を検出。事前補正testの旧revision 3登録を本番共通descriptor登録へ変更（2件）、履歴testの根拠なし任意HorseIdを証明可能な名前由来IDへ変更（1件）。履歴のCorePersisted=trueを追加確認し、HTTP 200だけで成功扱いしない。関連23件が再検証成功。元の同名だけによる手動ID結合を復活させない。

## Production observation (read-only)

2026-09-27 06:50 JST: pipelineは03:08 JSTから別の`jockey-profile / JraCollectionException`（引退騎手一覧の公開リンクなし、group `02D78CBC10B10F67`）で自動停止。horseの公開検索NoCandidateも14件ある。本変更の馬主ID不一致と一括に扱わない。未知障害が残る間は停止解除を行わない。AC6の配備・一意対象の復旧準備は継続できるが、実worker成功と後続進捗の証拠はこの停止により未達となり得る。別原因の修正・安全境界変更は自動で本scopeへ追加しない。

## Independent review

AC2/4/6のidentity・lease/hold・owner限定復旧をread-only explorer `identity_safety_review`へ依頼（requested gpt-6-sol、observed model/tokenは取得不可）。Mainが統合判断を保持。結果とclosureはREADMEの検証記録へ反映する。
