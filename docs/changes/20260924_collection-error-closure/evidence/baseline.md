# 2026-09-24 実行baseline

本番操作はGETのみ。既存DPAPI runner使用可能、19:08:33 JSTで29 findings / 27 actionable。GET group一覧は9群・1001通知（上限10000、一覧時点）。finding分類と通知groupは異なる集計。
基準worktree: `C:/Users/yuto.nagano/.codex/worktrees/collection-error-closure/HorseRacingPrediction`。

## Revision / patch matrix

| 対象 | 確認 |
| --- | --- |
| clean基準 | origin/main `ff95b22499312462194a970b038dcc9d959163bd` |
| PR64 | 基準に包含。dispatch順序契約を変更しない |
| PR65 | 基準に包含。Result selector/Card独立/wake/代替開催schemaを保持 |
| metadata | `e036a6b8`のStore/test差分を適用。最新mainのexplicit URL登録とlease表示修正を保持 |
| API配備revision | unknown。healthはrevisionを提供しない |
| Collector配備revision | unknown。AWS get-functionは認証期限切れ、再認証依頼済み |
| production schema | unknown。ローカルmodel差分なし・空SQLite全migration成功は本番証明ではない |

## 現在停止

- pipeline paused、updatedAt 2026-09-23 23:51:34.560 JST。
- task `8886fd12-8ace-4786-a4ab-1ac6405d96e4`、resource `discovery:2026092103`、race-discovery、JraNavigationException。
- group `24C9B062F8829D62`、同根拠2通知（もう一つはdiscovery:2026092100）。
- batch `c9bef9df-1108-49ca-9455-918ccbef08ed`。attempt `88ca298b-6569-42c6-a168-30396c9cb7f0`。
- API履歴のrequestedUrl/finalUrl/httpStatusCodeはnull。pageIdentificationはdefinition/resourceのみ。保存済み履歴から遷移段階は復元できない。
- 最新main実サイト試験で9/21 NakayamaはHistorical開催選択で同じ失敗。同日Hanshin・9/22 Nakayama・直近/2020 Resultは成功。
- 公式日別番組と代替案内から9/21中山中止→9/22代替を確認。月間カレンダーparserは中止状態を読まずコース名だけ抽出し、discoveryが旧日を探す。配備revision同一性は未確認だが、現在コードでも原因を再現。
- 次の安全な操作: [中止開催の追加設計](../decisions/cancelled-meeting-discovery.md)承認後に日付/競馬場単位の公式状態を扱う。未知navigation failureを一律隔離しない。
- parent T1b/T2f、AC1/4/5/7。既存T3 Codex `01a0bcb6-49a3-7d12-841e-f557ef046960` と同じ統合解消範囲、追加の永続taskは未作成。

## 原因群と残る証拠

| group / count | 分類・原証拠 | 次のgate |
| --- | --- | --- |
| 24C9B062F8829D62 / 2 | 中止旧日のdiscovery、new-defect | T2fの追加設計承認→実装→T3 |
| 42610E8E41734116 / 1 | race-detail 20260921:Nakayama:6、旧日のnavigation failure | PR65の既存reschedule経路の配備確認、旧source identityを保った限定復旧計画。日付書換え禁止 |
| 8905EC8E89C51E4C / 89 | Trainer SubjectResourceMissing | 現行preflight/retirement対象確認、未知ID補正は未承認 |
| 3AB26D0AF847C12A / 482 | Horse SubjectResourceMissing、代表8a6f3666-2c09-4e96-bdd9-a23d9a16cd22 | 同上。通知消去で解消としない |
| 47887865414A82A5 / 43 | Jockey SubjectResourceMissing | 同上 |
| E56D934CCA551769 / 111 | Horse SubjectNotIdentified、代表9b084dbe-4bb9-41ef-bf88-0eb717b5c779、SubjectIdentification:NoCandidate | name/identityの安全なpreviewと入力証拠が必要。metadata修正だけで全件回復とは言わない |
| E92F33DCE435144B / 3 | Trainer SubjectNotIdentified | 同上 |
| BA97EBE92E4D55E1 / 266 | Owner SubjectNotIdentified、代表80aa70ca-3a9c-4e60-b74b-082415381ae4、SubjectIdentification:OwnerNotRegistered | T1c/T2f。現行名前projection不在を確認、同一人物/aliasを推測しない。既存task履歴metadataはGET summaryに未公開、追加証拠/個別補正設計が必要 |
| 15848E15AD3B6CCF / 4 | Trainer PlaywrightException、代表64bc5520-3ab8-49b9-a2d1-2f98260cfd62 | 配備revisionと安全に編集された段階証拠をAWS再認証後に照合。TargetClosedと断定しない |

全groupは現時点で未解消。本番再開・Recovery・データ補正・deployは未実施。
Owner/profileの現行preflight/修復契約の変更や新規aliasをこのcheckpointでは実装しない。

## 独立reviewから検出した関連欠陥

1. Current Resultを無操作でスキップするstageがNotApplicableで、StoreがUnavailableへ降格。SQLite+実handlerテストで再現→no-op stageを出さず保存証拠を維持。
2. leaseのCurrent判定がAppliedRevisionを見ないため新revision要求も収集省略。Card/Resultとも要求版数未達をlease上Dueとし、実handler→Storeで両方を再収集・新版保存する反例を追加。

これは承認済みAC2/4の局所欠陥の閉鎖であり、新しい停止policyやデータ補正ではない。
