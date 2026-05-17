namespace HorseRacingPrediction.AgentClient.Scheduling;

public static class AgentDashboardHtmlRenderer
{
    public static string Render()
    {
        return
            """
<!DOCTYPE html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>AgentClient Dashboard</title>
  <style>
    :root {
      --bg: #f3efe6;
      --panel: rgba(255,255,255,0.82);
      --line: rgba(39, 48, 39, 0.14);
      --text: #203126;
      --muted: #5d6c62;
      --ok: #2f7d4a;
      --warn: #a36e17;
      --bad: #9d3c2d;
      --run: #0d6e6e;
      --chip: #e7e0d2;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0;
      font-family: "Hiragino Sans", "Yu Gothic UI", sans-serif;
      color: var(--text);
      background:
        radial-gradient(circle at top left, rgba(184, 213, 190, 0.55), transparent 32%),
        radial-gradient(circle at top right, rgba(225, 197, 142, 0.45), transparent 28%),
        linear-gradient(180deg, #f7f3eb 0%, var(--bg) 100%);
    }
    .wrap {
      width: min(1380px, calc(100% - 32px));
      margin: 0 auto;
      padding: 28px 0 56px;
    }
    .hero {
      display: flex;
      justify-content: space-between;
      align-items: end;
      gap: 16px;
      margin-bottom: 22px;
    }
    h1 {
      margin: 0;
      font-size: clamp(28px, 4vw, 46px);
      letter-spacing: 0.02em;
    }
    .sub {
      color: var(--muted);
      margin-top: 8px;
    }
    .actions {
      display: flex;
      gap: 12px;
      align-items: center;
      flex-wrap: wrap;
    }
    .filter {
      display: flex;
      flex-direction: column;
      gap: 6px;
      font-size: 12px;
      color: var(--muted);
    }
    select {
      border: 1px solid var(--line);
      border-radius: 12px;
      padding: 8px 10px;
      background: rgba(255,255,255,0.88);
      color: var(--text);
      min-width: 132px;
    }
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      backdrop-filter: blur(10px);
      border-radius: 20px;
      box-shadow: 0 18px 40px rgba(49, 53, 37, 0.08);
    }
    .metrics {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 16px;
      margin-bottom: 18px;
    }
    .metric {
      padding: 18px 20px;
      min-height: 118px;
    }
    .metric h2 {
      margin: 0 0 10px;
      font-size: 12px;
      color: var(--muted);
      text-transform: uppercase;
      letter-spacing: 0.12em;
    }
    .metric .value {
      font-size: 38px;
      font-weight: 700;
    }
    .metric .note {
      margin-top: 8px;
      color: var(--muted);
      font-size: 13px;
    }
    .grid {
      display: grid;
      grid-template-columns: 1.25fr 1fr;
      gap: 16px;
    }
    .section {
      padding: 18px 18px 10px;
    }
    .section h2 {
      margin: 0 0 12px;
      font-size: 18px;
    }
    .toolbar {
      display: flex;
      gap: 12px;
      align-items: center;
      color: var(--muted);
      margin-bottom: 12px;
      flex-wrap: wrap;
    }
    .table-wrap {
      overflow: auto;
      border-top: 1px solid var(--line);
    }
    table {
      width: 100%;
      border-collapse: collapse;
      min-width: 720px;
    }
    th, td {
      padding: 11px 10px;
      text-align: left;
      border-bottom: 1px solid rgba(39, 48, 39, 0.08);
      vertical-align: top;
      font-size: 13px;
    }
    th {
      color: var(--muted);
      font-size: 12px;
      position: sticky;
      top: 0;
      background: rgba(250,248,242,0.92);
      backdrop-filter: blur(6px);
    }
    .chips {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .chip {
      padding: 6px 10px;
      border-radius: 999px;
      background: var(--chip);
      font-size: 12px;
      color: var(--text);
    }
    .status {
      display: inline-flex;
      align-items: center;
      gap: 6px;
      padding: 5px 9px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0.02em;
      background: #ebe7de;
    }
    .status.ok { color: var(--ok); background: rgba(47,125,74,0.13); }
    .status.warn { color: var(--warn); background: rgba(163,110,23,0.14); }
    .status.bad { color: var(--bad); background: rgba(157,60,45,0.12); }
    .status.run { color: var(--run); background: rgba(13,110,110,0.12); }
    .muted { color: var(--muted); }
    .error {
      white-space: pre-wrap;
      color: var(--bad);
      max-width: 420px;
    }
    .mono { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; }
    .footer {
      margin-top: 18px;
      color: var(--muted);
      font-size: 12px;
    }
    button {
      border: 0;
      border-radius: 999px;
      background: #264733;
      color: #fffdf8;
      padding: 7px 12px;
      font-size: 12px;
      cursor: pointer;
    }
    button.secondary {
      background: #8b6d38;
    }
    button:disabled {
      opacity: 0.45;
      cursor: default;
    }
    @media (max-width: 980px) {
      .metrics, .grid { grid-template-columns: 1fr; }
    }
  </style>
</head>
<body>
  <div class="wrap">
    <div class="hero">
      <div>
        <h1>AgentClient Dashboard</h1>
        <div class="sub">結果ジョブ、日別抽出状態、取得障害を AgentClient ローカル状態から可視化します。</div>
      </div>
      <div class="actions">
        <div class="chip">自動更新: 30 秒</div>
        <div class="chip">表示期間: 180 日</div>
        <label class="filter">
          <span>対象月</span>
          <select id="monthFilter"></select>
        </label>
        <label class="filter">
          <span>Job Status</span>
          <select id="jobStatusFilter"></select>
        </label>
        <label class="filter">
          <span>Day Status</span>
          <select id="dayStatusFilter"></select>
        </label>
      </div>
    </div>

    <div class="metrics">
      <div class="panel metric">
        <h2>Running Jobs</h2>
        <div class="value" id="runningJobs">-</div>
        <div class="note" id="jobBreakdown">-</div>
      </div>
      <div class="panel metric">
        <h2>Retry Scheduled Days</h2>
        <div class="value" id="retryDays">-</div>
        <div class="note" id="dayBreakdown">-</div>
      </div>
      <div class="panel metric">
        <h2>Dead Letters</h2>
        <div class="value" id="deadLetters">-</div>
        <div class="note">job + day status の合計</div>
      </div>
      <div class="panel metric">
        <h2>Acquisition Failures</h2>
        <div class="value" id="acquisitionFailures">-</div>
        <div class="note">horse / jockey / trainer</div>
      </div>
    </div>

    <div class="grid">
      <div class="panel section">
        <h2>Job Statuses</h2>
        <div class="toolbar">
          <span class="muted" id="jobsUpdated">読込中...</span>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>JobType</th>
                <th>Status</th>
                <th>Priority</th>
                <th>Attempts</th>
                <th>Updated</th>
                <th>Error</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody id="jobsBody"></tbody>
          </table>
        </div>
      </div>

      <div class="panel section">
        <h2>Result Day Statuses</h2>
        <div class="toolbar">
          <span class="muted" id="daysUpdated">読込中...</span>
        </div>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Date</th>
                <th>Status</th>
                <th>Done</th>
                <th>Retry</th>
                <th>Reason</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody id="daysBody"></tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="grid" style="margin-top:16px;">
      <div class="panel section">
        <h2>Race Collection Statuses</h2>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Race</th>
                <th>Card</th>
                <th>Result</th>
                <th>Updated</th>
              </tr>
            </thead>
            <tbody id="racesBody"></tbody>
          </table>
        </div>
      </div>

      <div class="panel section">
        <h2>Acquisition Statuses</h2>
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th>Subject</th>
                <th>Operation</th>
                <th>Status</th>
                <th>Updated</th>
                <th>Error</th>
              </tr>
            </thead>
            <tbody id="acquisitionsBody"></tbody>
          </table>
        </div>
      </div>
    </div>

    <div class="footer">AgentClient local dashboard</div>
  </div>

  <script>
    const jobStatusNames = {
      0: 'Pending',
      1: 'Ready',
      2: 'Running',
      3: 'WaitingDependency',
      4: 'Succeeded',
      5: 'Failed',
      6: 'Cancelled',
      7: 'DeadLetter'
    };
    const dayStatusNames = {
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
    const collectionStateNames = {
      0: 'Unknown',
      1: 'Running',
      2: 'Succeeded',
      3: 'Failed',
      4: 'DeadLetter'
    };
    const statusClass = (status) => {
      const normalized = normalizeStatus(status);
      if (!normalized) return 'status';
      const s = normalized.toLowerCase();
      if (s.includes('succeed') || s.includes('complete')) return 'status ok';
      if (s.includes('run')) return 'status run';
      if (s.includes('retry') || s.includes('partial') || s.includes('wait')) return 'status warn';
      if (s.includes('dead') || s.includes('fail') || s.includes('incomplete')) return 'status bad';
      return 'status';
    };

    const fmt = (value) => value ? new Date(value).toLocaleString('ja-JP') : '-';
    const dashboardState = {
      month: 'all',
      jobStatus: 'all',
      dayStatus: 'all'
    };
    const dashboardData = {
      jobs: [],
      days: [],
      races: [],
      acquisitions: []
    };
    const today = new Date();
    const from = new Date(today.getTime() - 179 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
    const to = today.toISOString().slice(0, 10);

    async function load() {
      const [jobs, days, races, acquisitions] = await Promise.all([
        fetchJson('/agent/job-statuses?limit=80'),
        fetchJson(`/agent/result-day-statuses?from=${from}&to=${to}`),
        fetchJson(`/agent/race-collection-statuses?from=${from}&to=${to}`),
        fetchJson(`/agent/acquisition-statuses?from=${from}&to=${to}`)
      ]);

      dashboardData.jobs = jobs;
      dashboardData.days = days;
      dashboardData.races = races;
      dashboardData.acquisitions = acquisitions;

      syncFilterOptions(jobs, days, races, acquisitions);
      applyFiltersAndRender();
    }

    async function fetchJson(url) {
      try {
        const response = await fetch(url);
        if (!response.ok) {
          return [];
        }

        return await response.json();
      } catch {
        return [];
      }
    }

    async function requeueJob(jobType, deduplicationKey) {
      const response = await fetch(`/agent/job-statuses/${encodeURIComponent(jobType)}/${encodeURIComponent(deduplicationKey)}/requeue`, {
        method: 'POST'
      });
      await ensureSucceeded(response);
      await load();
    }

    async function requeueDay(providerType, targetDate, mode) {
      const response = await fetch(`/agent/result-day-statuses/${encodeURIComponent(providerType)}/${encodeURIComponent(targetDate)}/requeue?mode=${encodeURIComponent(mode)}`, {
        method: 'POST'
      });
      await ensureSucceeded(response);
      await load();
    }

    async function ensureSucceeded(response) {
      if (response.ok) {
        return;
      }

      let message = `${response.status} ${response.statusText}`;
      try {
        const payload = await response.json();
        if (payload?.errors) {
          message = Object.values(payload.errors).flat().join('\n');
        }
      } catch {
      }

      window.alert(message);
      throw new Error(message);
    }

    function applyFiltersAndRender() {
      const filteredJobs = filterJobs(dashboardData.jobs);
      const filteredDays = filterDays(dashboardData.days);
      const filteredRaces = filterByMonth(dashboardData.races, item => item.raceDate, item => item.updatedAt);
      const filteredAcquisitions = filterByMonth(dashboardData.acquisitions, _ => null, item => item.updatedAt);

      renderJobs(filteredJobs);
      renderDays(filteredDays);
      renderRaces(filteredRaces);
      renderAcquisitions(filteredAcquisitions);
      renderMetrics(filteredJobs, filteredDays, filteredAcquisitions);
    }

    function filterJobs(jobs) {
      return jobs.filter(job => {
        const monthMatched = matchesMonth(job.updatedAt);
        const statusMatched = dashboardState.jobStatus === 'all' || normalizeJobStatus(job.status) === dashboardState.jobStatus;
        return monthMatched && statusMatched;
      });
    }

    function filterDays(days) {
      return days.filter(day => {
        const monthMatched = matchesMonth(day.targetDate);
        const statusMatched = dashboardState.dayStatus === 'all' || normalizeDayStatus(day.status) === dashboardState.dayStatus;
        return monthMatched && statusMatched;
      });
    }

    function filterByMonth(items, primaryDateSelector, fallbackDateSelector) {
      return items.filter(item => {
        const primaryDate = primaryDateSelector(item);
        const fallbackDate = fallbackDateSelector(item);
        return matchesMonth(primaryDate ?? fallbackDate);
      });
    }

    function matchesMonth(value) {
      if (dashboardState.month === 'all') {
        return true;
      }

      if (!value) {
        return false;
      }

      return String(value).slice(0, 7) === dashboardState.month;
    }

    function syncFilterOptions(jobs, days, races, acquisitions) {
      syncSelect(
        document.getElementById('monthFilter'),
        ['all', ...collectMonthOptions(jobs, days, races, acquisitions)],
        dashboardState.month,
        value => value === 'all' ? '全期間' : value,
        value => { dashboardState.month = value; applyFiltersAndRender(); });

      syncSelect(
        document.getElementById('jobStatusFilter'),
        ['all', ...collectStatusOptions(jobs)],
        dashboardState.jobStatus,
        value => value === 'all' ? 'すべて' : value,
        value => { dashboardState.jobStatus = value; applyFiltersAndRender(); });

      syncSelect(
        document.getElementById('dayStatusFilter'),
        ['all', ...collectStatusOptions(days)],
        dashboardState.dayStatus,
        value => value === 'all' ? 'すべて' : value,
        value => { dashboardState.dayStatus = value; applyFiltersAndRender(); });
    }

    function syncSelect(element, values, currentValue, labelSelector, onChange) {
      const nextValue = values.includes(currentValue) ? currentValue : values[0];
      if (element.dataset.initialized !== 'true') {
        element.addEventListener('change', event => onChange(event.target.value));
        element.dataset.initialized = 'true';
      }

      element.innerHTML = values
        .map(value => `<option value="${value}">${labelSelector(value)}</option>`)
        .join('');
      element.value = nextValue;
      onChange(nextValue);
    }

    function collectMonthOptions(jobs, days, races, acquisitions) {
      const values = [
        ...jobs.map(item => item.updatedAt),
        ...days.map(item => item.targetDate),
        ...races.map(item => item.raceDate),
        ...acquisitions.map(item => item.updatedAt)
      ]
        .filter(Boolean)
        .map(value => String(value).slice(0, 7));

      return [...new Set(values)].sort().reverse();
    }

    function collectStatusOptions(items) {
      return [...new Set(items.map(item => normalizeStatus(item.status)).filter(Boolean))].sort();
    }

    function renderJobs(jobs) {
      document.getElementById('jobsUpdated').textContent = `件数: ${jobs.length}`;
      document.getElementById('jobsBody').innerHTML = jobs.map(job => `
        <tr>
          <td class="mono">${job.jobType}<div class="muted mono">${job.deduplicationKey}</div></td>
          <td><span class="${statusClass(job.status)}">${normalizeJobStatus(job.status)}</span></td>
          <td>${job.priority}</td>
          <td>${job.attemptCount}</td>
          <td>${fmt(job.updatedAt)}</td>
          <td class="error">${job.lastError ?? ''}</td>
          <td>${normalizeJobStatus(job.status) === 'DeadLetter' || normalizeJobStatus(job.status) === 'Failed' ? `<button onclick="requeueJob('${escapeValue(job.jobType)}', '${escapeValue(job.deduplicationKey)}')">再投入</button>` : ''}</td>
        </tr>`).join('');
    }

    function renderDays(days) {
      document.getElementById('daysUpdated').textContent = `件数: ${days.length}`;
      document.getElementById('daysBody').innerHTML = days.map(day => `
        <tr>
          <td class="mono">${day.targetDate}</td>
          <td><span class="${statusClass(day.status)}">${normalizeDayStatus(day.status)}</span></td>
          <td>${day.completedRaceCount} / ${day.expectedRaceCount}</td>
          <td>${fmt(day.retryAfter)}</td>
          <td class="error">${day.incompleteReason ?? day.lastError ?? ''}</td>
          <td>${normalizeDayStatus(day.status) === 'RetryScheduled' || normalizeDayStatus(day.status) === 'Incomplete' || normalizeDayStatus(day.status) === 'DeadLetter' ? `<div class="chips"><button class="secondary" onclick="requeueDay('${escapeValue(day.providerType)}', '${escapeValue(day.targetDate)}', 'Discovery')">探索から再投入</button><button onclick="requeueDay('${escapeValue(day.providerType)}', '${escapeValue(day.targetDate)}', 'Collection')">収集のみ再投入</button></div>` : ''}</td>
        </tr>`).join('');
    }

    function renderRaces(races) {
      document.getElementById('racesBody').innerHTML = races.slice(-80).reverse().map(race => `
        <tr>
          <td class="mono">${race.raceDate} ${race.racecourse} ${race.raceNumber}R<div>${race.raceName ?? ''}</div></td>
          <td><span class="${statusClass(race.raceCardStatus)}">${normalizeCollectionStatus(race.raceCardStatus)}</span></td>
          <td><span class="${statusClass(race.raceResultStatus)}">${normalizeCollectionStatus(race.raceResultStatus)}</span></td>
          <td>${fmt(race.updatedAt)}</td>
        </tr>`).join('');
    }

    function renderAcquisitions(items) {
      document.getElementById('acquisitionsBody').innerHTML = items.slice(0, 80).map(item => `
        <tr>
          <td>${item.subjectType}<div>${item.subjectName}</div></td>
          <td>${item.operationType}</td>
          <td><span class="${statusClass(item.status)}">${normalizeCollectionStatus(item.status)}</span></td>
          <td>${fmt(item.updatedAt)}</td>
          <td class="error">${item.errorReason ?? ''}</td>
        </tr>`).join('');
    }

    function renderMetrics(jobs, days, acquisitions) {
      const runningJobs = jobs.filter(x => normalizeJobStatus(x.status) === 'Running').length;
      const deadLetters = jobs.filter(x => normalizeJobStatus(x.status) === 'DeadLetter').length + days.filter(x => normalizeDayStatus(x.status) === 'DeadLetter').length;
      const retryDays = days.filter(x => normalizeDayStatus(x.status) === 'RetryScheduled').length;
      const incompleteDays = days.filter(x => normalizeDayStatus(x.status) === 'Incomplete' || normalizeDayStatus(x.status) === 'Partial').length;
      const acquisitionFailures = acquisitions.filter(x => normalizeCollectionStatus(x.status) === 'Failed' || normalizeCollectionStatus(x.status) === 'DeadLetter').length;

      document.getElementById('runningJobs').textContent = runningJobs;
      document.getElementById('jobBreakdown').textContent = `Ready: ${jobs.filter(x => normalizeJobStatus(x.status) === 'Ready').length} / Waiting: ${jobs.filter(x => normalizeJobStatus(x.status) === 'WaitingDependency').length}`;
      document.getElementById('retryDays').textContent = retryDays;
      document.getElementById('dayBreakdown').textContent = `Incomplete: ${incompleteDays}`;
      document.getElementById('deadLetters').textContent = deadLetters;
      document.getElementById('acquisitionFailures').textContent = acquisitionFailures;
    }

    function normalizeStatus(value) {
      if (value === null || value === undefined) {
        return '';
      }

      if (typeof value === 'string') {
        return value;
      }

      return String(value);
    }

    function normalizeJobStatus(value) {
      if (typeof value === 'number' && jobStatusNames[value] !== undefined) {
        return jobStatusNames[value];
      }

      return normalizeStatus(value);
    }

    function normalizeDayStatus(value) {
      if (typeof value === 'number' && dayStatusNames[value] !== undefined) {
        return dayStatusNames[value];
      }

      return normalizeStatus(value);
    }

    function normalizeCollectionStatus(value) {
      if (typeof value === 'number' && collectionStateNames[value] !== undefined) {
        return collectionStateNames[value];
      }

      return normalizeStatus(value);
    }

    function escapeValue(value) {
      return String(value ?? '').replaceAll('\\', '\\\\').replaceAll("'", "\\'");
    }

    load().catch(err => console.error(err));
    setInterval(() => load().catch(err => console.error(err)), 30000);
  </script>
</body>
</html>
""";
    }
}