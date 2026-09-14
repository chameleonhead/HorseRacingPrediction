# 収集タスクへ同定メタデータを引き継ぐ

- Status: Implemented
- Owner: Main
- Created: 2026-09-14
- Updated: 2026-09-14

## Context

関連情報の発見から作成された調教師プロフィール収集で、タスクのResource IDだけは保持された一方、発見時の調教師名や発見元が実行時・障害表示で確認できなかった。現行実装は依頼時の `attributes` をResource行へ保存し、Workerのlease取得時にResourceの現在値を読む。Task自身にはスナップショットがなく、後続依頼による上書き、空の再依頼、復旧経路の違いによって、作成時に存在したメタデータと実行時の入力が一致する保証がない。

未登録の調教師IDが発行される直接経路も確認した。競走馬プロフィール収集が成功すると、`JraSubjectProfileCollectionHandler.DiscoverHorseReferencesAsync` がプロフィール項目の「調教師」文字列から `DeterministicIdGenerator.BuildEntityId("trainer", name)` を呼び、そのIDで `trainer-profile` の収集依頼を直接作成する。この経路にはTrainer業務データの存在確認・登録がない。その後、プロフィール保存APIは対象Trainerが未登録なら404を返すため、同定に成功しても保存できない。今回のジョブの依頼理由「関連情報の発見」、Background優先度、発見元情報の欠落はこの経路と一致する。

レース由来の経路では、レース保存時に騎手・調教師を自動登録してから関連収集依頼を作るが、登録側と依頼側が別々に名前からIDを再生成している。空白・表記正規化が異なると別IDになり得るため、こちらも「登録済みであること」を依頼作成の契約として保証できていない。

## Goals

- すべての収集タスクが、Task IDとResource keyに加えて、作成時の収集メタデータを不変スナップショットとして保持する。
- 関連主体の発見では、少なくとも表示名と発見元Resourceを渡し、取得できる場合は提供元URL・提供元識別子も渡す。
- 再試行、配送の再実行、障害復旧でも元タスクのメタデータを失わない。
- 主体同定失敗時に、運用者が対象名と発見元をジョブ詳細で確認できる。
- Horse/Jockey/Trainer/Ownerの関連収集では、収集Resource IDを場当たり的に再生成せず、業務データへ登録・解決されたcanonical IDを使用する。

## Non-goals

- 氏名一致、生年月日一致、提供元識別子一致などの同定条件は緩和しない。
- 同名候補を自動的に統合しない。
- 既存のResource IDおよびTask IDを置換しない。
- HTML本文、Cookie、認証情報、任意のページ内容をメタデータとして保存しない。

## Experience and interaction design

ジョブ詳細の「収集対象」に「タスクメタデータ」を追加する。公開して安全な既知キーのみを日本語ラベルで表示し、値がない項目は省略する。主体ジョブでは少なくとも対象名と発見元を表示する。同定失敗の技術情報には従来のエラーコード等を維持し、対象名を含む構造化された同定診断を表示する。

## Documentation updates

- `docs/22-collector-design.md`: Task単位の不変メタデータ、Resource属性との役割分担、発見ジョブの必須キー、旧Taskの互換読み込みをCollection Platformの正本設計として追記する。

## Technical impact

- `collection_tasks` にTask作成時のメタデータJSON列を追加する。新規Task、bulk、recovery、revision起因、依存タスクなど全Task生成経路で初期化する。
- Resource属性は主体の最新既知情報として非破壊的に統合する。Taskメタデータは依頼固有値を優先して作成時に固定し、lease取得後はTask列を正本とする。
- 既存TaskはTask列が空の場合に限りResource属性を読むことで互換性を保つ。再実行・復旧で新Taskを作る場合は、元Taskのスナップショットを引き継いで補正値を上書きする。
- 発見元の標準キーは `name`、`sourceIdentity`、`sourceUrl`、`discoveredFromType`、`discoveredFromProvider`、`discoveredFromId` とする。取得できないURLや提供元識別子は捏造せず省略する。
- 主体発見producerは共通の登録・解決APIを経由し、正規化済み名称、利用可能な提供元識別子、既存aliasを使ってcanonical IDを取得してから収集依頼を作る。新規主体なら同じ処理内で業務データを先に登録する。登録・解決に失敗した場合は、実行不能なプロフィール収集Taskを作らず、親Task側に具体的な失敗を返す。
- 文字列長、キー数、許可キーを境界で検証し、機密情報や無制限payloadを保存・配送しない。

## Decisions

- Resource属性だけを実行入力にする現行方式を廃止し、Task作成時スナップショットをWorker入力の正本にする。これにより、同じResourceへの後続依頼が実行中・履歴Taskの意味を変えない。
- 既存の `attributes` APIは互換維持し、内部で標準化・検証したTask metadataとして扱う。外部契約の一括改名は行わない。
- 表示名が取得できる主体発見では `name` を必須とする。Horse/Jockey/Trainer/Ownerすべてに同じ規則を適用する。
- 発見元は親Task IDだけでなくResource type/provider/idを保持する。Task IDは実行追跡用であり、業務上の発見元を表す代替にはしない。
- 決定論的ID生成は登録・解決コンポーネントの内部に集約する。各producerが表示名から独自にIDを再生成することを禁止する。

## Acceptance criteria

| ID | Observable criterion | Tasks | Verification | State |
| --- | --- | --- | --- | --- |
| AC1 | 通常、bulk、関連発見、手動再取得、Recovery、revision/依存生成の各経路で、新規Taskが作成時メタデータを保持し、配送後のWorkerが同じ値を受け取る。 | T1, T2 | Store/transport integration tests | Verified |
| AC2 | 同じResourceへの後続依頼がResource属性を更新しても、既存Taskと既存試行が参照するメタデータは変化しない。 | T1 | Persistence regression test | Verified |
| AC3 | Race由来およびプロフィール由来のHorse/Jockey/Trainer/Owner発見ジョブは、取得済みの対象名と発見元Resourceを持ち、利用可能な提供元URL・識別子も保持する。 | T2 | Discovery handler tests | Verified |
| AC4 | 主体同定が0件・複数候補・プロフィール不一致で失敗した場合、ジョブ詳細から対象名と発見元を確認でき、エラー分類は引き続き `SubjectNotIdentified` のままである。 | T3 | Handler and component tests | Verified |
| AC5 | 再試行、lease再取得、重複配送、障害Recoveryでメタデータが欠落せず、補正値が指定された場合だけ当該キーを上書きする。 | T1, T2 | Retry/recovery integration tests | Verified |
| AC6 | 既存DBを移行後、Taskメタデータ列が空の旧TaskはResource属性による互換読み込みができ、新規TaskはTask列だけで実行できる。 | T1 | Migration and compatibility tests | Verified |
| AC7 | 未知キー、上限超過値、認証情報に該当するキーは依頼境界で拒否され、HTML本文や秘密情報がジョブ詳細・配送payloadへ出ない。 | T1, T3 | Validation and UI allowlist tests | Verified |
| AC8 | Horse/Jockey/Trainerの関連プロフィールTaskが作成された時点で同じcanonical IDの業務データが存在し、Owner identity Taskは既存Ownerまたは明示的な未解決identity対象へ結び付く。 | T2 | API/collector end-to-end tests | Verified |
| AC9 | 競走馬プロフィール由来の調教師発見とレース由来の調教師発見が、表記正規化後に同じ人物なら同じcanonical IDを使い、未登録IDだけのTaskを作らない。 | T2 | Cross-producer identity test | Verified |

## Delivery plan

1. 永続モデル、移行、Task生成・lease取得・Recoveryのスナップショット規則を実装する。
2. すべての関連主体発見producerで標準メタデータを組み立てる。
3. 同定診断とジョブ詳細の安全な表示を接続する。
4. 実 transport/persistence 境界を含む回帰テスト、build、format、CodeGraph同期を行う。

## Task plan

| ID | Task | Owner | Model tier | Depends on | Write scope | Verification | Completion evidence | State |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| T1 | Task metadataの永続化、検証、全Task生成経路、lease/retry/recovery互換を実装する（AC1, AC2, AC5, AC6, AC7）。 | Main | High capability | - | CollectionOperations、migration、store tests | targeted store/migration tests | schema v11、store tests | Verified |
| T2 | 共通の主体登録・解決契約を追加し、全主体発見producerとWorker入力をcanonical ID・標準メタデータへ接続する（AC1, AC3, AC5, AC8, AC9）。 | Main | High capability | T1 | API、Collector、scraping models、collector/API tests | handler/transport/end-to-end tests | entity writer接続、full Collector tests | Verified |
| T3 | 同定失敗診断とジョブ詳細へ安全なメタデータ表示を追加する（AC4, AC7）。 | Main | High capability | T1, T2 | API contracts/UI、component tests | API/component tests | allowlist UI、API tests | Verified |

## Review gates

- **Design and task-split review** — Reviewer: Main。Inputs: 現行 `RequestAsync`、Task/Resource entity、lease取得、関連主体producer、同定handler、業務データ登録API、既存収集設計。Decision: Resourceの可変属性をTask入力に使う再現性欠如と、producerが登録なしにIDを生成する参照整合性欠如は別々に解消する必要がある。AC1–AC9はT1–T3と永続・transport・handler・API・UIの検証へ追跡可能。共有Store/主体登録契約への依存が強いため委譲せずMainが直列実装する。Follow-up: ユーザー承認後にpre-implementation reviewを記録し、T1から開始する。
- **Pre-implementation review** — Reviewer: Main。Inputs: Approved統合record、T1–T3。Decision: schema/store、主体producer、UIの順に直列実装し、外部状態は変更しない。
- **Checkpoint review** — Reviewer: Main。Inputs: schema v11、全Task生成site、entity writer、UI allowlist、focused/full tests。Decision: build失敗でMetadataJsonをRequest entityへ誤配置した問題とschema version期待値を修正し、全回帰成功後に採用した。
- **Final review** — Reviewer: Main。Inputs: AC1–AC9、T1–T3、全diff、format/build/solution tests。Decision: 全項目Verified。Task metadataは新Taskの正本、旧TaskだけResource fallbackとなり、主体登録とUI表示までproduction pathへ接続したためImplementedとする。

## Verification record

- 設計時調査: `RequestAsync` は依頼attributesを `CollectionResourceEntity.AttributesJson` へ保存・上書きし、`AcquireAsync` はTaskではなくResourceの現在値から `LeasedCollectionTask.Attributes` を生成することを確認。
- 設計時調査: Race由来の主体発見は `name` と `requestedByRaceId` を渡すが、Jockey/Trainerの提供元識別子は保持しておらず、プロフィール由来の発見は `name` と深さ情報だけで発見元Resourceを渡していないことを確認。
- 設計時調査: `DiscoverHorseReferencesAsync` は馬プロフィールから調教師名を抽出後、Trainer登録APIを呼ばずに名称ベースのIDを生成して収集依頼する。対してプロフィール保存endpointは既存Trainerを要求するため、未登録ID Taskが成立する競合ではなく確定的な経路になっている。
- 設計時調査: レース保存endpointにはTrainer自動登録がある一方、関連収集producerは登録結果IDを受け取らず名称から再生成するため、正規化差で未登録IDを作る余地がある。
- 2026-09-15: schema v11でnullable `MetadataJson` をTaskへ追加し、新規Task、bulk、follow-up、旧definition migrationの全生成siteへスナップショットを保存した。旧TaskだけResource属性を読む。
- 2026-09-15: metadataは許可キー、32キー、値2048文字、総量8192文字へ制限し、secret/token/cookie/authorization/html相当キーを拒否した。
- 2026-09-15: プロフィール由来Trainer/Horseを `IDataCollectionWriteService` で登録・解決してから子Taskを作り、Race/Subject producerへ発見元Resource metadataを追加した。
- 2026-09-15: ジョブ詳細は公開可能な既知キーだけを日本語ラベルで表示する。
- 2026-09-15: `dotnet format HorseRacingPrediction.sln --no-restore --verify-no-changes`、Release build、非External solution test成功。Contracts 43、Domain 96、Application 56、Infrastructure 13、MachineLearning 14、Agents 106、Scraping 224、Collector 195、API 203（skip 1）。

## Deviations and follow-up

- 設計との差分は、独立した新規subject resolve APIではなく、既存の `IDataCollectionWriteService` が持つatomicなUpsert境界を再利用した点である。canonical IDと業務データ登録を同じ既存経路へ揃え、公開契約を増やさずAC8/AC9を満たす。
