namespace HorseRacingPrediction.AgentClient.JraTesting;

public static class JraJsonTesterPageHtml
{
    public const string Content = """
<!doctype html>
<html lang="ja">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>JRA Tool - AgentClient Operations</title>
  <style>
    :root {
      --bg: #f4efe5;
      --panel: rgba(255, 252, 247, 0.92);
      --ink: #1f2933;
      --muted: #5f6c76;
      --accent: #005f73;
      --accent-2: #bb3e03;
      --line: rgba(0, 95, 115, 0.16);
      --shadow: 0 24px 60px rgba(31, 41, 51, 0.12);
    }

    * { box-sizing: border-box; }

    body {
      margin: 0;
      font-family: "Hiragino Sans", "Yu Gothic", sans-serif;
      color: var(--ink);
      background:
        radial-gradient(circle at top left, rgba(0, 95, 115, 0.14), transparent 38%),
        radial-gradient(circle at bottom right, rgba(187, 62, 3, 0.12), transparent 34%),
        linear-gradient(180deg, #faf7f1 0%, var(--bg) 100%);
      min-height: 100vh;
    }

    main {
      width: min(1080px, calc(100% - 32px));
      margin: 40px auto;
      display: grid;
      gap: 20px;
    }

    .hero,
    .panel {
      background: var(--panel);
      border: 1px solid var(--line);
      border-radius: 24px;
      box-shadow: var(--shadow);
      backdrop-filter: blur(12px);
    }

    .hero {
      padding: 28px;
    }

    h1 {
      margin: 0 0 10px;
      font-size: clamp(28px, 5vw, 42px);
      line-height: 1.05;
      letter-spacing: -0.04em;
    }

    .lead {
      margin: 0;
      color: var(--muted);
      font-size: 15px;
      line-height: 1.7;
      max-width: 780px;
    }

    .panel {
      padding: 24px;
    }

    form {
      display: grid;
      gap: 16px;
    }

    label {
      display: grid;
      gap: 8px;
      font-weight: 700;
    }

    input[type="url"] {
      width: 100%;
      padding: 16px 18px;
      border-radius: 16px;
      border: 1px solid rgba(0, 95, 115, 0.18);
      font-size: 15px;
      color: var(--ink);
      background: rgba(255, 255, 255, 0.94);
    }

    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: center;
    }

    .toggle {
      display: inline-flex;
      gap: 10px;
      align-items: center;
      font-weight: 500;
      color: var(--muted);
    }

    button {
      border: 0;
      border-radius: 999px;
      padding: 14px 22px;
      font-size: 15px;
      font-weight: 700;
      cursor: pointer;
      transition: transform 120ms ease, opacity 120ms ease;
    }

    button:hover { transform: translateY(-1px); }
    button:disabled { opacity: 0.55; cursor: wait; transform: none; }

    .primary {
      background: linear-gradient(135deg, var(--accent) 0%, #0a9396 100%);
      color: white;
    }

    .sample {
      background: rgba(0, 95, 115, 0.08);
      color: var(--accent);
    }

    .nav-link {
      display: inline-flex;
      border-radius: 999px;
      padding: 10px 16px;
      background: rgba(0, 95, 115, 0.1);
      color: var(--accent);
      text-decoration: none;
      font-weight: 700;
      margin-top: 14px;
    }

    .output-head {
      display: flex;
      justify-content: space-between;
      gap: 16px;
      align-items: center;
      margin-bottom: 12px;
    }

    .status {
      color: var(--muted);
      font-size: 14px;
    }

    pre {
      margin: 0;
      padding: 18px;
      overflow: auto;
      border-radius: 18px;
      background: #12232e;
      color: #e8f1f2;
      min-height: 420px;
      font-size: 13px;
      line-height: 1.6;
    }

    @media (max-width: 640px) {
      main {
        width: min(100% - 20px, 1080px);
        margin: 20px auto 28px;
      }

      .hero,
      .panel {
        border-radius: 20px;
        padding: 18px;
      }
    }
  </style>
</head>
<body>
  <main>
    <section class="hero">
      <h1>JRA URL JSON Tester</h1>
      <p class="lead">JRA の URL を入力すると、ページ種別を判定し、対応する scraper または structured parser の JSON を返します。出馬表、オッズ、結果、プロフィール系に加えて、一覧系ページは structured parser の結果を確認できます。</p>
      <a class="nav-link" href="/tools">運用ポータルへ戻る</a>
    </section>

    <section class="panel">
      <form id="form">
        <label>
          JRA URL
          <input id="url" name="url" type="url" required value="https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1008202603070320260516/CB" placeholder="https://www.jra.go.jp/...">
        </label>

        <div class="row">
          <label class="toggle">
            <input id="includeSnapshot" type="checkbox">
            スナップショットも含める
          </label>
          <button type="submit" class="primary" id="submit">JSON を取得</button>
          <button type="button" class="sample" data-url="https://www.jra.go.jp/JRADB/accessD.html?CNAME=pw01dde1008202603070320260516/CB">出馬表サンプル</button>
          <button type="button" class="sample" data-url="https://www.jra.go.jp/JRADB/accessP.html?CNAME=pw01sde1008202603070320260517/2E">結果サンプル</button>
        </div>
      </form>
    </section>

    <section class="panel">
      <div class="output-head">
        <strong>Response</strong>
        <span class="status" id="status">待機中</span>
      </div>
      <pre id="output">ここに JSON が表示されます。</pre>
    </section>
  </main>

  <script>
    const form = document.getElementById('form');
    const urlInput = document.getElementById('url');
    const includeSnapshot = document.getElementById('includeSnapshot');
    const output = document.getElementById('output');
    const status = document.getElementById('status');
    const submit = document.getElementById('submit');

    for (const button of document.querySelectorAll('[data-url]')) {
      button.addEventListener('click', () => {
        urlInput.value = button.dataset.url;
      });
    }

    form.addEventListener('submit', async (event) => {
      event.preventDefault();

      const url = urlInput.value.trim();
      if (!url) {
        status.textContent = 'URL を入力してください';
        return;
      }

      submit.disabled = true;
      status.textContent = '取得中';
      output.textContent = '';

      try {
        const response = await fetch(`/api/tools/jra-json?url=${encodeURIComponent(url)}&includeSnapshot=${includeSnapshot.checked}`);
        const json = await response.json();
        output.textContent = JSON.stringify(json, null, 2);
        status.textContent = response.ok ? '取得完了' : `エラー ${response.status}`;
      } catch (error) {
        output.textContent = String(error);
        status.textContent = '通信エラー';
      } finally {
        submit.disabled = false;
      }
    });
  </script>
</body>
</html>
""";
}