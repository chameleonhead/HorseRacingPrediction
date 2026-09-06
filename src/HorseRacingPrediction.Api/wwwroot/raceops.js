window.raceOps = {
    focusById(id) {
        document.getElementById(id)?.focus();
    }
};

// Blazor Serverの回線(Circuit)が"rejected"（認証Cookie失効等でサーバー側から
// 再接続を拒否された）状態になった場合、既定の"Rejoin failed... trying again in
// .. seconds"の無限リトライ表示のまま止まってしまう（rejectedは本来リロードで
// 復帰する想定だが、Cookie失効時はリロード後も同じ401を再現し続けるため、
// 見た目上ループしているように見える）。rejected を検知したらリトライさせず、
// 直接ログイン画面へ遷移させる。
window.addEventListener('components-reconnect-state-changed', function (event) {
    if (event.detail?.state === 'rejected') {
        window.location.href = '/login';
    }
});
