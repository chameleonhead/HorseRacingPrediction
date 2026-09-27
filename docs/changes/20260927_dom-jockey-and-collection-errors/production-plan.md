# 本番対応案（未実行・追加承認が必要）

## 確定した差分

- [補正preview](evidence/correction-preview.json)はGETだけで生成。中山11Rの16頭を公式Horse identity・保存taskのレース由来metadata・既存Horse IDで照合した。馬番は照合キーにしていない。
- 15件のJockey ID参照を正しいDOM名のIDへ変更、坂井瑠星の1件は不変。Race/Horse/Entry ID、予想/結果、旧master、過去attemptを変更・削除しない。各旧/新ID・job/requestはJSONに列挙済み。
- 実行直前に公式出馬表とAPIを再読取し、previewと一致しなければ更新して再判断する。新しい騎手変更などを古いpreviewで上書きしない。
- 全レースを走査したpreviewではない。今回の一括補正対象は中山11Rだけとし、他レースは新revisionの通常再取得による更新と別の差分確認を区別する。

## 段階1: 停止を維持して修正版を配備

Linux CI成功後、PRをmergeし既存GitHub ActionsだけでAPIとcollectorを同一SHAへ配備する。Actionsのpause/drain、API停止後のSQLite/WAL検査とbackup、healthチェックを使用する。現在の停止を維持し、自動再開しない。本番APIキーをGitHub本文・commit・ログへ入れない。

配備後はGETでSHA/health/pipelineを確認する。backupがない、drain失敗、異なるSHA、health不良なら補正・再開へ進まない。戻す場合もGitHub経由で修正版をrevertする。旧コードへ戻しても誤騎手参照を復活させるデータrollbackは自動実行しない。

## 段階2: 対象を絞って補正・再取得

| 対象 | 対応 | 保全・前提 |
| --- | --- | --- |
| 中山11R / race-65ab3995-5853-5270-aa59-ac6b9b83d15d | 新parser/保存経路で出馬表を再取得し15参照を更新 | 最新preview一致、馬/Entry ID不変、旧masterの全体改名なし |
| 阪神4R / 20260927:Hanshin:4 | 既存Ready task e4d8bdb7-4022-4f65-a382-6ee3e85566cbを利用し結果取得 | 新しい重複requestを作らず、現在の完全な結果を検証して保存 |
| ハクタカ、レジームチェンジ、プラチナリーフ、リキマル、ヴェントリナ | 保存済み公式identityを保持して取得 | 各ID/source/taskは調査JSONに列挙。候補名だけでは置換しない |
| ディープインパクト | DOM名を使い再取得 | 旧名義馬IDを保持、画像alt除外。保存時のidentity/birth検証を維持 |
| 血統の8件 | 手動復旧対象から除外し未同定を保持 | アニマルキングダム、Corniche、Maximus Mischief、ディスクリートキャット、フォーウィールドライブ、タリスマニック、クリソベリル、ジャンダルム。名前/血統関係/失敗を削除しない |

旧騎手jobの処理は、出馬表が正しくなったことと別に確認する。誤名の旧requestを再送してNotApplicableにするだけでは復旧扱いしない。15旧IDの他レース参照と旧job状態を再照合し、正しい代替requestと対応づく場合だけ履歴を保ったsupersede/suppressを行う。既存の主体修復APIが該当エラーを受理するか、fingerprint/一意性の安全条件を満たすかを配備後previewで確認する。未対応なら汎用DB更新で迂回せず、対象限定修復経路の設計・承認・実装を追加する。この判断まで全体再開は行わない。

既存SubjectIdentificationAutoRecoveryは、Horseの候補0件/複数候補をrevision上昇だけで復旧候補にしない（structural/profile-name mismatch等だけ）。8件の自動同定が改善したとは扱わない。

## 段階3: 再開と完了判定

全体再開は別の明示承認の後。対象の実worker成功、保存後の15騎手参照、同名別馬の非保存、失敗履歴保全を確認する。新しい原因不明のsystemic failureで停止したら解除を反復しない。正常な後続batchまたは15分の進捗まで観測し、先行馬主46件のAC6へ成功証拠を還元する。8未同定件は理由付き保留として報告し、エラーゼロや完全復旧とは表現しない。

## 現時点の承認境界

プログラム修正・ローカル/CI検証・read-only previewは今回の承認内。上記の本番配備、補正、旧jobの抑止、再要求、再開はいずれも未実行。まず段階1（停止維持の配備）の承認を依頼し、配備後の対象確認を経て段階2・3の実行内容を確定する。
