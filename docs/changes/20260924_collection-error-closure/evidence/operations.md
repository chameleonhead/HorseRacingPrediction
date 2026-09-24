# 公開・配備・安定稼働確認

## 2026-09-24 公開開始

利用者の「変更をプッシュした上で、安定稼働まで面倒を見てもらえますか？」により、承認済み修正のbranch/commit/pushを許可。現在の基準mainはff95b224でfetch後も不変。
独立worktreeの本変更だけを専用branch `codex/collection-error-closure` へ公開する。元作業ツリーのUI/Program/skill等の変更は含めない。

以前のActions禁止はまだ撤回されていない。app-ciはPR/main、app-deployはmainで起動するため、専用branchへのpushのみ先行しPR/main反映は確認待ち。既存の収集監視Actionsは復活させない。
AWS read-only get-functionは認証期限切れ。利用者へaws loginを依頼済み。認証完了を推測せず、配備revision/schema/rollbackを確認する前に本番変更しない。

## 残るgate

1. 利用者のAWS再認証と、今回のテスト/配備Actionsの利用可否。
2. O1-O3: API/Collector/DBの版・互換性、backup/rollback、配備先の確認。Actions以外を選ぶ場合も同じgateを満たす。
3. O4-O7: 代表通知を原因別1件、初回最大5件に限定し、対象と安全条件を明示してRecovery/resumeの操作承認を確認する。履歴削除・曖昧な主体ID補正・一括再実行はしない。
4. 本番の対象終端・独立後続成功・2周期以上の再停止なし・金曜Card/結果鮮度を実測。公開/merge/配備だけでImplementedにしない。

MainがGit公開と本番操作gateを直列所有。今回新規coding/委譲なし、既存reviewと実経路の証拠を引き継ぎ、push前にformat/build/非External全体testを再実行する。
