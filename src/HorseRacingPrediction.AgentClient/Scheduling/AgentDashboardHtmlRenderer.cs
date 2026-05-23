namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentDashboardHtmlRenderer
{
    public static string Render()
    {
        return
            """
<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AgentClient Operations Tool</title>
  <style>
    :root {
      --bg: #f6f3ed;
      --panel: rgba(255, 255, 255, 0.92);
      --line: rgba(34, 56, 67, 0.14);
      --ink: #1f2a31;
      --muted: #5e6a72;
      --accent: #0f766e;
      --danger: #a32020;
      --warn: #986200;
      --ok: #246d38;
      --shadow: 0 18px 40px rgba(0, 0, 0, 0.08);
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      color: var(--ink);
      font-family: "Hiragino Sans", "Yu Gothic", sans-serif;
      background:
        radial-gradient(circle at top right, rgba(15, 118, 110, 0.12), transparent 30%),
        radial-gradient(circle at top left, rgba(23, 88, 136, 0.09), transparent 28%),
        linear-gradient(180deg, #fbf9f4, var(--bg));
      min-height: 100vh;
    }

    main {
      width: min(1280px, calc(100% - 32px));
      margin: 28px auto 52px;
      display: grid;
      gap: 16px;
    }

    .hero,
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 20px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(8px);
    }

    .hero {
      padding: 24px;
      display: grid;
      gap: 14px;
    }

    h1 {
      margin: 0;
      font-size: clamp(28px, 4vw, 42px);
      letter-spacing: -0.02em;
    }

    .lead {
      margin: 0;
      color: var(--muted);
      line-height: 1.65;
      max-width: 860px;
    }

    .tablist {
      display: flex;
      gap: 10px;
      flex-wrap: wrap;
    }

    .tab-btn {
      border: 1px solid var(--line);
      border-radius: 999px;
      background: white;
      color: var(--ink);
      font-weight: 700;
      cursor: pointer;
      padding: 10px 16px;
    }

    .tab-btn.active {
      background: linear-gradient(135deg, var(--accent), #127baf);
      color: white;
      border-color: transparent;
    }

    .panel {
      padding: 18px;
      display: grid;
      gap: 12px;
    }

    .tab-panel { display: none; }
    .tab-panel.active { display: grid; }

    .status {
      font-size: 14px;
      color: var(--muted);
    }

    .metrics {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 10px;
    }

    .metric {
      border: 1px solid var(--line);
      border-radius: 14px;
      padding: 12px;
      background: rgba(255, 255, 255, 0.84);
    }

    .metric .label {
      font-size: 12px;
      color: var(--muted);
      margin-bottom: 4px;
    }

    .metric .value {
      font-size: 28px;
      font-weight: 800;
      line-height: 1.15;
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 12px;
    }

    h2, h3 {
      margin: 0;
      font-size: 18px;
    }

    h3 { font-size: 16px; }

    .hint {
      margin: 0;
      color: var(--muted);
      font-size: 14px;
      line-height: 1.6;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 13px;
    }

    th, td {
      text-align: left;
      padding: 8px 10px;
      border-bottom: 1px solid var(--line);
      vertical-align: top;
    }

    th { color: var(--muted); font-size: 12px; }

    .table-wrap {
      overflow: auto;
      max-height: 320px;
      border: 1px solid var(--line);
      border-radius: 12px;
      background: rgba(255, 255, 255, 0.9);
    }

    .pill {
      display: inline-block;
      padding: 2px 8px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      border: 1px solid var(--line);
      background: #f5f5f5;
    }

    .pill.bad { color: var(--danger); }
    .pill.warn { color: var(--warn); }
    .pill.ok { color: var(--ok); }
    .pill.run { color: var(--accent); }

    .actions { display: flex; gap: 10px; flex-wrap: wrap; }

    button, .link-btn {
      border: 0;
      border-radius: 999px;
      padding: 11px 16px;
      font-size: 14px;
      font-weight: 700;
      cursor: pointer;
      text-decoration: none;
      display: inline-flex;
      align-items: center;
      color: white;
      background: linear-gradient(135deg, var(--accent), #127baf);
    }

    .subtle {
      background: rgba(15, 118, 110, 0.12);
      color: #0f5f5a;
    }

    label {
      display: grid;
      gap: 6px;
      font-size: 13px;
      color: var(--muted);
    }

    input {
      border: 1px solid var(--line);
      border-radius: 10px;
      background: white;
      padding: 10px 12px;
      color: var(--ink);
    }

    .inline-form {
      display: grid;
      grid-template-columns: 1fr 1fr auto;
      gap: 10px;
      align-items: end;
    }

    .json {
      margin: 0;
      border-radius: 12px;
      padding: 12px;
      overflow: auto;
      min-height: 280px;
      background: #0f1720;
      color: #e2e8f0;
      border: 1px solid #0b1320;
      font-size: 12px;
      line-height: 1.6;
    }

    @media (max-width: 980px) {
      .metrics,
      .grid,
      .inline-form { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <main>
    <section class="hero">
      <h1>AgentClient Operations Tool</h1>
      <p class="lead">監視・デバッグ・情報収集を一体化した運用サイトです。日々のジョブ状況確認、手動再実行、API 保存データの可視化を同じ導線で扱えます。</p>
      <div class="tablist">
        <button class="tab-btn active" data-tab="monitor">監視</button>
        <button class="tab-btn" data-tab="debug">デバッグ</button>
        <button class="tab-btn" data-tab="insight">情報収集</button>
      </div>
      <div class="status" id="globalStatus">読込中...</div>
    </section>

    <section class="panel tab-panel active" id="tab-monitor">
      <h2>監視ダッシュボード</h2>
      <div class="metrics">
        <div class="metric"><div class="label">稼働中ワーク</div><div class="value" id="metricRunning">-</div></div>
        <div class="metric"><div class="label">待機ジョブ</div><div class="value" id="metricQueue">-</div></div>
        <div class="metric"><div class="label">Dead Letter</div><div class="value" id="metricDead">-</div></div>
        <div class="metric"><div class="label">失敗/不完全</div><div class="value" id="metricIssue">-</div></div>
      </div>
      <div class="grid">
        <div class="panel">
          <h3>現在進行中</h3>
          <div class="table-wrap">
            <table>
              <thead><tr><th>種別</th><th>対象</th><th>状態</th><th>更新</th></tr></thead>
              <tbody id="runningBody"></tbody>
            </table>
          </div>
        </div>
        <div class="panel">
          <h3>再試行・待機キュー</h3>
          <div class="table-wrap">
            <table>
              <thead><tr><th>種別</th><th>対象</th><th>状態</th><th>次回</th></tr></thead>
              <tbody id="queueBody"></tbody>
            </table>
          </div>
        </div>
      </div>
      <div class="panel">
        <h3>直近エラー/要対応</h3>
        <div class="table-wrap">
          <table>
            <thead><tr><th>種別</th><th>対象</th><th>状態</th><th>詳細</th><th>操作</th></tr></thead>
            <tbody id="issueBody"></tbody>
          </table>
        </div>
      </div>
    </section>

    <section class="panel tab-panel" id="tab-debug">
      <h2>デバッグ & 手動実行</h2>
      <p class="hint">監視画面と同じデータ基盤を使って、日次結果と予想ジョブを即時投入できます。解析ツールへの導線もここに統合しています。</p>
      <div class="grid">
        <div class="panel">
          <h3>日次結果を再取得</h3>
          <div class="inline-form">
            <label>対象日<input type="date" id="debugResultDate"></label>
            <label>Provider<input type="text" id="debugProvider" value="JRA"></label>
            <button id="triggerResult">再取得を開始</button>
          </div>
        </div>
        <div class="panel">
          <h3>予想ジョブを投入</h3>
          <div class="inline-form">
            <label>RaceId<input type="text" id="debugRaceId" placeholder="race-20260517-tokyo-11r"></label>
            <label>備考<input type="text" value="手動投入" disabled></label>
            <button id="triggerPrediction">予想ジョブ投入</button>
          </div>
        </div>
      </div>
      <div class="actions">
        <a class="link-btn" href="/tools/jra-tool" target="_blank" rel="noopener noreferrer">JRA 解析ツールを開く</a>
        <button class="subtle" id="refreshNow">監視データを即時更新</button>
      </div>
    </section>

    <section class="panel tab-panel" id="tab-insight">
      <h2>情報収集（API データ可視化）</h2>
      <p class="hint">保存済み API 情報の確認に使える簡易ビューアです。相対パスを指定して JSON を確認できます。今後の可視化機能追加時もこのセクションに統合可能です。</p>
      <div class="inline-form">
        <label>API Path<input type="text" id="inspectPath" value="/agent/job-statuses?limit=30"></label>
        <label>説明<input type="text" value="GET のみ" disabled></label>
        <button id="inspectButton">取得</button>
      </div>
      <pre class="json" id="inspectOutput">ここに API レスポンスが表示されます。</pre>
    </section>
  </main>

  <script>
    const state = { jobs: [], days: [], races: [], acquisitions: [] };
    const JOB_STATUS_LIMIT = 200;

    const statusMap = {
      0: 'Pending',
      1: 'Ready',
      2: 'Running',
      3: 'WaitingDependency',
      4: 'Succeeded',
      5: 'Failed',
      6: 'Cancelled',
      7: 'DeadLetter'
    };

    const dayStatusMap = {
      0: 'NotStarted',
      1: 'Discovering',
      2: 'Ready',
      3: 'Running',
      4: 'Partial',
      5: 'Incomplete',
      6: 'Complete',
      7: 'RetryScheduled',
      8: 'DeadLetter'
    };

    const now = new Date();
    const from = new Date(now.getTime() - 179 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
    const to = now.toISOString().slice(0, 10);

    document.getElementById('debugResultDate').value = to;

    for (const button of document.querySelectorAll('.tab-btn')) {
      button.addEventListener('click', () => switchTab(button.dataset.tab));
    }

    document.getElementById('triggerResult').addEventListener('click', triggerResult);
    document.getElementById('triggerPrediction').addEventListener('click', triggerPrediction);
    document.getElementById('refreshNow').addEventListener('click', refresh);
    document.getElementById('inspectButton').addEventListener('click', runInspector);
    document.getElementById('issueBody').addEventListener('click', onIssueActionClicked);

    async function refresh() {
      try {
        const [jobs, days, races, acquisitions] = await Promise.all([
          fetchJson(`/agent/job-statuses?limit=${JOB_STATUS_LIMIT}`),
          fetchJson(`/agent/result-day-statuses?from=${from}&to=${to}`),
          fetchJson(`/agent/race-collection-statuses?from=${from}&to=${to}`),
          fetchJson(`/agent/acquisition-statuses?from=${from}&to=${to}`)
        ]);

        state.jobs = jobs;
        state.days = days;
        state.races = races;
        state.acquisitions = acquisitions;
        renderMonitor();
        document.getElementById('globalStatus').textContent = `最終更新: ${new Date().toLocaleString('ja-JP')}`;
      } catch (error) {
        document.getElementById('globalStatus').textContent = `読込失敗: ${error}`;
      }
    }

    async function runInspector() {
      const path = document.getElementById('inspectPath').value.trim();
      const output = document.getElementById('inspectOutput');
      if (!path.startsWith('/')) {
        output.textContent = '相対パスは / から開始してください。';
        return;
      }

      output.textContent = '取得中...';
      try {
        const response = await fetch(path);
        if (!response.ok) {
          const body = await response.text();
          output.textContent = `HTTP ${response.status}\\n${body}`;
          return;
        }
        const payload = await response.json();
        output.textContent = JSON.stringify(payload, null, 2);
      } catch (error) {
        output.textContent = String(error);
      }
    }

    async function triggerResult() {
      const targetDate = document.getElementById('debugResultDate').value;
      const providerType = document.getElementById('debugProvider').value || 'JRA';
      if (!targetDate) {
        alert('対象日を指定してください。');
        return;
      }

      await post(`/agent/result-day-jobs/trigger?targetDate=${encodeURIComponent(targetDate)}&providerType=${encodeURIComponent(providerType)}`);
      await refresh();
    }

    async function triggerPrediction() {
      const raceId = document.getElementById('debugRaceId').value.trim();
      if (!raceId) {
        alert('RaceId を指定してください。');
        return;
      }

      await post(`/agent/prediction-jobs/trigger?raceId=${encodeURIComponent(raceId)}`);
      await refresh();
    }

    async function requeueJob(jobType, key) {
      await post(`/agent/job-statuses/${encodeURIComponent(jobType)}/${encodeURIComponent(key)}/requeue`);
      await refresh();
    }

    async function requeueDay(providerType, targetDate) {
      await post(`/agent/result-day-statuses/${encodeURIComponent(providerType)}/${encodeURIComponent(targetDate)}/requeue?mode=discovery`);
      await refresh();
    }

    async function onIssueActionClicked(event) {
      const button = event.target.closest('button[data-action]');
      if (!button) {
        return;
      }

      const action = button.dataset.action;
      if (action === 'requeue-job') {
        await requeueJob(
          decodeURIComponent(button.dataset.jobType ?? ''),
          decodeURIComponent(button.dataset.deduplicationKey ?? ''));
      } else if (action === 'requeue-day') {
        await requeueDay(
          decodeURIComponent(button.dataset.providerType ?? ''),
          decodeURIComponent(button.dataset.targetDate ?? ''));
      }
    }

    function renderMonitor() {
      const jobs = state.jobs || [];
      const days = state.days || [];
      const races = state.races || [];
      const acquisitions = state.acquisitions || [];

      const runningJobs = jobs.filter(x => normalizeJobStatus(x.status) === 'Running');
      const runningDays = days.filter(x => ['Discovering', 'Running'].includes(normalizeDayStatus(x.status)));
      const queueJobs = jobs.filter(x => ['Pending', 'Ready', 'WaitingDependency', 'Failed'].includes(normalizeJobStatus(x.status)));
      const queueDays = days.filter(x => ['NotStarted', 'Ready', 'RetryScheduled', 'Incomplete', 'Partial'].includes(normalizeDayStatus(x.status)));
      const deadLetters = jobs.filter(x => normalizeJobStatus(x.status) === 'DeadLetter').length + days.filter(x => normalizeDayStatus(x.status) === 'DeadLetter').length;

      const raceIssues = races.filter(x => ['Failed', 'DeadLetter'].includes(normalizeState(x.raceCardStatus)) || ['Failed', 'DeadLetter'].includes(normalizeState(x.raceResultStatus)));
      const acquisitionIssues = acquisitions.filter(x => ['Failed', 'DeadLetter'].includes(normalizeState(x.status)));

      document.getElementById('metricRunning').textContent = runningJobs.length + runningDays.length;
      document.getElementById('metricQueue').textContent = queueJobs.length + queueDays.length;
      document.getElementById('metricDead').textContent = deadLetters;
      document.getElementById('metricIssue').textContent = raceIssues.length + acquisitionIssues.length;

      document.getElementById('runningBody').innerHTML = [
        ...runningJobs.slice(0, 30).map(item => row('Job', item.jobType, normalizeJobStatus(item.status), item.updatedAt)),
        ...runningDays.slice(0, 30).map(item => row('Day', `${item.providerType}:${item.targetDate}`, normalizeDayStatus(item.status), item.updatedAt))
      ].join('') || '<tr><td colspan="4">該当なし</td></tr>';

      document.getElementById('queueBody').innerHTML = [
        ...queueJobs.slice(0, 30).map(item => row('Job', item.jobType, normalizeJobStatus(item.status), item.retryAfter ?? item.updatedAt)),
        ...queueDays.slice(0, 30).map(item => row('Day', `${item.providerType}:${item.targetDate}`, normalizeDayStatus(item.status), item.retryAfter ?? item.updatedAt))
      ].join('') || '<tr><td colspan="4">該当なし</td></tr>';

      const issues = [
        ...jobs.filter(x => ['Failed', 'DeadLetter'].includes(normalizeJobStatus(x.status))).slice(0, 20).map(item => issueJobRow(item)),
        ...days.filter(x => ['Failed', 'DeadLetter', 'Incomplete'].includes(normalizeDayStatus(x.status))).slice(0, 20).map(item => issueDayRow(item))
      ];
      document.getElementById('issueBody').innerHTML = issues.join('') || '<tr><td colspan="5">該当なし</td></tr>';
    }

    function issueJobRow(item) {
      const status = normalizeJobStatus(item.status);
      const error = escapeHtml(item.lastError ?? '(error detail なし)');
      const hasDeduplicationKey = item.deduplicationKey !== null
        && item.deduplicationKey !== undefined
        && String(item.deduplicationKey).length > 0;
      const canRetry = hasDeduplicationKey && status !== 'Running';
      const encodedJobType = encodeURIComponent(item.jobType ?? '');
      const encodedDeduplicationKey = encodeURIComponent(item.deduplicationKey ?? '');
      return `<tr>
        <td>Job</td>
        <td>${escapeHtml(item.jobType)}<div class="status">${escapeHtml(item.deduplicationKey ?? '-')}</div></td>
        <td>${statusPill(status)}</td>
        <td>${error}</td>
        <td>${canRetry ? `<button class="subtle" data-action="requeue-job" data-job-type="${encodedJobType}" data-deduplication-key="${encodedDeduplicationKey}">再投入</button>` : '-'}</td>
      </tr>`;
    }

    function issueDayRow(item) {
      const status = normalizeDayStatus(item.status);
      const error = escapeHtml(item.lastError ?? item.incompleteReason ?? '(理由なし)');
      const encodedProviderType = encodeURIComponent(item.providerType ?? '');
      const encodedTargetDate = encodeURIComponent(item.targetDate ?? '');
      return `<tr>
        <td>Result Day</td>
        <td>${escapeHtml(item.providerType)}:${escapeHtml(item.targetDate)}</td>
        <td>${statusPill(status)}</td>
        <td>${error}</td>
        <td><button class="subtle" data-action="requeue-day" data-provider-type="${encodedProviderType}" data-target-date="${encodedTargetDate}">再投入</button></td>
      </tr>`;
    }

    function row(kind, target, status, updatedAt) {
      return `<tr>
        <td>${escapeHtml(kind)}</td>
        <td>${escapeHtml(target)}</td>
        <td>${statusPill(status)}</td>
        <td>${fmt(updatedAt)}</td>
      </tr>`;
    }

    function switchTab(tab) {
      for (const button of document.querySelectorAll('.tab-btn')) {
        button.classList.toggle('active', button.dataset.tab === tab);
      }
      for (const panel of document.querySelectorAll('.tab-panel')) {
        panel.classList.toggle('active', panel.id === `tab-${tab}`);
      }
    }

    function statusPill(status) {
      const normalized = (status || '').toLowerCase();
      let clazz = 'pill';
      if (['deadletter', 'failed', 'incomplete'].includes(normalized)) clazz += ' bad';
      else if (['partial', 'retryscheduled', 'discovering', 'ready', 'pending', 'waitingdependency', 'notstarted'].includes(normalized)) clazz += ' warn';
      else if (['running'].includes(normalized)) clazz += ' run';
      else clazz += ' ok';
      return `<span class="${clazz}">${escapeHtml(status || '-')}</span>`;
    }

    function normalizeJobStatus(value) {
      return typeof value === 'number' ? (statusMap[value] ?? String(value)) : String(value ?? '');
    }

    function normalizeDayStatus(value) {
      return typeof value === 'number' ? (dayStatusMap[value] ?? String(value)) : String(value ?? '');
    }

    function normalizeState(value) {
      return String(value ?? '');
    }

    function fmt(value) {
      return value ? new Date(value).toLocaleString('ja-JP') : '-';
    }

    function escapeHtml(value) {
      return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
    }

    async function fetchJson(url) {
      const response = await fetch(url);
      if (!response.ok) {
        throw new Error(`${url} -> ${response.status}`);
      }
      return response.json();
    }

    async function post(url) {
      const response = await fetch(url, { method: 'POST' });
      if (!response.ok) {
        const body = await response.text();
        throw new Error(`${response.status}: ${body}`);
      }
      return response;
    }

    refresh();
    setInterval(refresh, 30000);
  </script>
</body>
</html>
""";
    }
}
