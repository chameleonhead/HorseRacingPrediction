# URL失敗時の名称フォールバックと障害ジョブ再開

- Status: Implemented
- Owner: HorseRacingPrediction maintainers
- Created: 2026-09-13
- Updated: 2026-09-13

## Context

競走馬・騎手・調教師プロフィールのhandlerは、保存済みLocationを順に試し、成功しなければ `ToSubjectProfileAsync` による名称探索へ進む。ただし競走馬の探索では、Task属性の `sourceIdentity` を引き続き必須一致条件として使用する。失敗したURLが `sourceIdentity` と同じ場合、名称検索で正しい候補を発見してもURL不一致として除外し、`SubjectNotIdentified` になる。

障害復旧は同一Resource/Definitionのactive taskがあればそのTaskへまとめる。通常は終端化時にactive rowを削除するが、過去の配送失敗や中断した復旧状態を含め、終端Taskを指す古いactive rowまたは `RecoveryInProgress` が残った場合、画面から再取得しても新しい実行可能Taskにならない可能性がある。また、現時点で大量に存在するURL関連Failureを修正版へ再投入する明示的な移行手順がない。

## Goals

- プロフィールURLの遷移、ページ種別、同一性検証に失敗した場合、URLへの依存を外して名称から取得する。
- 競走馬は名称に加えて生年月日がある場合は生年月日で同定し、同名の別馬を保存しない。
- 騎手・調教師は現役・引退名簿から正規化名称で一意に同定する。
- URL由来のFailure、`DeadLetter`、`DispatchAttemptsExceeded`、失敗したRecoveryを、新しいRecovery taskとして再開可能にする。
- 修正版配備後、現在未解決の対象Failureを安全に一括再投入できる。

## Non-goals

- 名称が一致しない、または同名候補を一意に絞れない対象を推測で保存すること。
- RaceCard / RaceResultの日時・競馬場・レース番号による既存ナビゲーションを「名称だけ」に置き換えること。
- 成功済み・解決済みFailureを無条件に再実行すること。
- pipeline停止中にWorker実行を開始すること。復旧要求は登録できるが停止解除まで配送しない。

## Experience and interaction design

通常のプロフィール取得では保存済みURLを優先する。URLが利用できない、異なる種類のページへ到達する、または対象本人ではない場合、そのLocation outcomeを記録して次のURLへ進む。すべて失敗したら、失敗した `sourceIdentity` を検索条件から外し、名称探索へ切り替える。

名称探索で一意に同定できれば取得を成功させ、実際に到達したURLを新しいActive Locationおよび `sourceIdentity` として保存する。失敗した旧LocationはSuspect/Invalidへ評価する。同定不能なら候補情報を残して `SubjectNotIdentified` とし、別人を保存しない。

ジョブ詳細と障害グループの再取得は、既存Taskを「再開」するのではなく、元Resource/Definition、revision、属性を引き継いだ新しいRecovery taskを作る。古いactive参照が終端Taskを指す場合は同じtransactionで修復する。実行中・待機中の正常なactive taskがある場合だけ、そのTaskへまとめる。

配備後の運用操作として、未解決のURL関連Failureと、実行可能Taskを持たない `RecoveryInProgress` をpreviewし、対象件数を確認してから一括Recoveryする。pipelineが停止中ならRecovery taskは待機し、運用者が停止原因を確認して再開した後に優先順位順で処理する。

## Documentation updates

- `docs/22-collector-design.md`: プロフィールLocation失敗時の名称フォールバックとRecovery task再作成規則を追記する。
- 本変更記録: フォールバック時の識別条件、既存Failureの再投入、再開不可能状態の修復に関する正本とする。

## Technical impact

- `JraSubjectProfileCollectionHandler`はLocation経由用identityと名称フォールバック用identityを分離する。
- 名称フォールバック用identityでは失敗した `sourceIdentity` をnullにし、名称と利用可能な生年月日を保持する。
- `JraNavigator.FindHorseAsync`の候補列挙・同名判定を既存の厳密検証とともに使用する。
- 成功した名称探索URLをLocation outcomeとResource locationへ反映する。
- `CollectionPlatformStore.RequestAsync`はactive rowが指すTaskの状態を検査し、terminalまたは不存在なら古いactive rowを除去して新Taskを作る。
- Failure recoveryはOpenに加え、紐付くRecovery taskがterminalまたは不存在の `RecoveryInProgress` を再選択可能にする。新Task IDへ対応状態を付け替える。
- 既存Failureの一括復旧は、既存のpreview付き障害グループRecoveryを使用し、URL関連分類と停止中の挙動をテストする。

## Decisions

- URL失敗とは、HTTP/ナビゲーション例外、ページ種別不一致、対象同一性不一致、parse/validation失敗を含む。キャンセルはフォールバックせず直ちに中断する。
- フォールバックは全Location候補が失敗した後に一度だけ行う。
- 競走馬で生年月日がある場合は「名称＋生年月日」の一意一致を必須とする。生年月日がなく名称候補が複数なら失敗する。
- `sourceIdentity` はURL候補の検証には使用するが、そのURL自体が失敗した後の名称探索を拘束しない。
- RaceCard / RaceResultは名称ではなく、既存どおり日付・競馬場・レース番号から通常ナビゲーションへフォールバックする。
- terminal taskを復活させてAttempt履歴を書き換えず、必ず新しいRecovery taskを作成する。

## Acceptance criteria

| ID | Observable criterion | State |
|---|---|---|
| AC1 | 競走馬の保存済みURLがエラーでも、名称＋生年月日で一意の候補を取得して保存できる。 | Verified |
| AC2 | 騎手・調教師の保存済みURLがエラーでも、公開名簿の正規化名称で一意の候補を取得して保存できる。 | Verified |
| AC3 | URLが別対象を返した場合は保存せず、名称フォールバックで同一性を再検証する。 | Verified |
| AC4 | 同名候補が複数で生年月日等で一意にできない場合は保存せず、候補付きの同定失敗を記録する。 | Verified |
| AC5 | URL失敗Locationと名称フォールバック成功LocationがAttempt詳細に記録され、成功URLが次回の優先Locationになる。 | Verified |
| AC6 | Failed、DeadLetter、DispatchAttemptsExceededのResourceを再取得すると、元属性を保持した新しいRecovery taskが作成される。 | Verified |
| AC7 | terminal・不存在Taskを指す古いactive rowがあってもatomicに修復され、新しいRecovery taskが作成される。 | Verified |
| AC8 | 失敗した／孤立したRecoveryInProgressを再Recoveryでき、新Task成功時に元FailureがResolvedになる。 | Verified |
| AC9 | 正常なactive taskがある場合は重複Taskを作らず、そのTaskへRecovery状態を関連付ける。 | Verified |
| AC10 | pipeline停止中はRecovery要求を保持するが配送・lease取得せず、再開後に実行する。 | Verified |
| AC11 | 現在未解決のURL関連Failureをpreview後に一括再投入し、対象件数、作成Task、再利用Taskを検証記録へ残す。 | Verified |
| AC12 | URL失敗→名称探索→保存のhandler統合テストと、terminal/stale recoveryのStore/API統合テストが成功する。 | Verified |

## Delivery plan

1. URL用identityと名称フォールバック用identityを分離し、成功Locationを返す。
2. Horse/Jockey/Trainer handlerテストにURLエラー、別対象、同名、生年月日、一意成功を追加する。
3. active task整合性修復とRecoveryInProgress再関連付けをStore transactionへ追加する。
4. Store/API/UIテストで単体・グループRecovery、停止中、並行要求を検証する。
5. 配備後に対象Failureをpreviewし、一括Recoveryを実施して進捗・成功・残存同定失敗を確認する。

## Verification record

- 設計前調査: subject handlerは全Location失敗後に名称探索へ進むが、同じ `sourceIdentity` を保持することを確認。
- 設計前調査: `FindHorseAsync`は `sourceIdentity` がある場合、名称一致候補でもURLが一致しなければ除外することを確認。
- 設計前調査: Recoveryはactive rowの存在だけで既存Taskへまとめ、参照先Taskの実行可能状態をRequest作成前に検証しないことを確認。
- URL候補失敗後のidentityから`sourceIdentity`だけを除去し、名称と生年月日を維持して通常探索へフォールバックするよう接続した。
- active rowがterminal・不存在Taskを指す場合、同じRequest transaction内で古い参照を削除して新しいRecovery taskを作るよう修正した。RecoveryInProgressも新Taskへ付け替える。
- URL失敗→名称・生年月日探索成功、terminal active参照修復、新Task lease取得の統合テストが成功した。
- `dotnet format HorseRacingPrediction.sln --no-restore`を実行した。Release buildは警告0・エラー0。Collector 160件成功、Api 184件成功・外部依存1件skip。
- 2026-09-15: 現行HEADの騎手・調教師handler、terminal/stale active row、RecoveryInProgress再関連付けと解決処理を再監査し、focused testsおよびRelease非External solution testsの成功によりAC2/AC8をVerifiedへ更新した。
- 2026-09-15: production preview run `34872379869` とapply run `34872777840` で主体同定Failure候補が合計0件（safe 0、blocked 0）であることを確認した。applyではpipelineをpauseしRunning taskのdrain後に再previewし、作成・再利用対象なしのままresumeしたため、AC11をVerifiedとした。

## Deviations and follow-up

- production previewで対象Failureが0件だったためRecovery taskは作成しなかった。0件を正常な収束結果として記録した。
