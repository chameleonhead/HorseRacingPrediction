# 任意文字を含むジョブIDを安全に参照・操作する

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-10
- Updated: 2026-09-10

## Context

ジョブ詳細画面と管理APIはジョブIDをURLパスセグメントへ埋め込んでいる。報告された `RaceReacquisition:RaceId { Date = 09/06/2026, ... }...` はID内に `/` を含み、URL上では `%2F` になる。エンコード済みスラッシュはリバースプロキシまたはHTTPサーバーでパス区切りとして扱われるか拒否されるため、BlazorルートやAPI endpointへ到達せずエラーになる。

ジョブIDは永続化済みの不透明な識別子であり、既存データを書き換えずに任意文字を安全に運べる経路が必要である。

## Goals

- `/`、空白、`{}`、`:`などを含む既存ジョブIDの詳細を管理画面から表示できる。
- 詳細取得、保留、保留解除、リラン、完了済み再取得の全操作で同じ安全なID伝達方式を使う。
- 一覧、親子関係、取得状況、再取得完了表示、SNS通知から生成するリンクを安全な形式へ統一する。
- 既存の単純なジョブID向けURL/APIを破壊せず段階移行する。

## Non-goals

- 既存ジョブIDの再採番やDB移行。
- ジョブID生成規則そのものの全面変更。
- 公開済みの旧URLをリバースプロキシ層で復元すること。

## Experience and interaction design

管理画面の正規URLを `/jobs/detail?jobId=<encoded>` とする。一覧、関連ジョブ、通知のリンクから遷移した利用者は従来と同じ詳細画面を表示し、URL形式の違いを意識しない。IDがない場合はジョブ一覧へ誘導し、存在しない場合は現行のNot Found表示を維持する。

報告された旧URLはサーバー到達前に拒否され得るため、自動リダイレクトを保証しない。新たに生成するリンクをすべて正規URLへ変更する。

## Documentation updates

- `docs/20-admin-ui-design.md`: ジョブIDを不透明値として扱い、詳細URLと全リンク生成でクエリ文字列を使う規則を正本へ追加する。
- `docs/22-collector-design.md`: ジョブ管理APIでIDをパスセグメントへ埋め込まない運用上の制約を追加する。

## Technical impact

- Blazorのジョブ詳細ページへ `/jobs/detail` ルートを追加し、`jobId`を`[SupplyParameterFromQuery]`で受け取る。安全な旧パスは互換用に維持する。
- 管理APIにクエリ文字列でIDを受け取る詳細・操作endpointを追加する。UIクライアントは新endpointだけを使用する。
- URL生成を共通helperへ集約し、`Uri.EscapeDataString`した値をクエリ値として配置する。
- SNS通知、一覧、親子リンク、取得状況、各再取得コンポーネント、リース中表示を新URLへ変更する。

## Decisions

### 採用: クエリ文字列でジョブIDを渡す

クエリ値では `%2F` がパス構造へ影響せず、既存IDを変換・再採番せずに扱える。

### 不採用: catch-allルート

プロキシがアプリ到達前にencoded slashを拒否する場合は解決せず、デコード後の区切りとIDの区別も不安定になる。

### 不採用: Base64 URL変換したパス

安全だが全リンクで双方向変換が必要になり、クエリ方式より複雑である。ジョブID自体を隠す要件もない。

## Acceptance criteria

1. 報告された形式のジョブIDを含む正規URLから詳細を表示できる。
2. `/`、空白、`{}`、`:`を含むIDで詳細取得、保留、解除、リラン、再取得が正しいジョブへ適用される。
3. 一覧、親子関係、取得状況、再取得表示、リース表示、SNS通知が正規URLを生成する。
4. IDなし、存在しないID、API失敗のloading/empty/error表示が維持される。
5. 単純なIDを使う既存APIと安全な旧画面URLの互換性を維持する。
6. API component tests、関連Blazor component tests、build、`git diff --check`が成功する。

## Delivery plan

1. クエリ型のジョブ詳細・操作APIとクライアントを追加する。
2. ジョブ詳細ページと全リンク生成元を正規URLへ移行する。
3. 特殊文字IDのAPI・コンポーネント回帰テストを追加する。
4. 検証結果と差分を本記録へ追記する。

## Verification record

- `dotnet build HorseRacingPrediction.sln --no-restore`: 成功（警告0、エラー0）。
- Apiテスト: 122件成功、1件スキップ。既存のジョブ詳細・保留テストを含む。
- `/`、空白、`{}`、`:`を含むジョブIDで、クエリ型の詳細取得と保留が成功するcomponent testを追加した。
- 再取得コンポーネントとSNS通知のリンク形式テスト: 成功。
- `git diff --check`: 成功。

## Deviations and follow-up

- 設計どおり実装済み。正規画面URLと管理UIクライアントをクエリ方式へ移行し、安全な旧ルート/APIは互換用に維持した。
- 報告された旧URL自体はプロキシで拒否され得るため、新形式URLを使用する。既存ジョブデータの移行は不要。
- 初回push後のCI/CDは、GitHub-hosted Ubuntuに既定登録されたGoogle Chrome APTリポジトリのミラー不整合でPlaywright依存導入前に失敗した。Playwright Chromiumには不要な同APTソースをCI内だけで無効化し、外部ミラーの公開競合が検証・デプロイを遮断しないよう修正した。
