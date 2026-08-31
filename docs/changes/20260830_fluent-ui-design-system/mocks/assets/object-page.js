(() => {
  const list = document.getElementById('list-screen');
  const detail = document.getElementById('detail-screen');
  const screens = [...document.querySelectorAll('[id$="-screen"]')];
  const title = detail?.querySelector('.object-detail-header h1');
  if (!list || !detail || !title) return;
  let scrollPosition = 0;
  const render = () => {
    const screenName = location.hash.slice(1) || 'list';
    const active = screens.find(screen => screen.id === `${screenName}-screen`) ?? list;
    screens.forEach(screen => { screen.hidden = screen !== active; });
    if (active !== list) scrollTo(0, 0);
    else requestAnimationFrame(() => scrollTo(0, scrollPosition));
  };
  document.querySelectorAll('[data-detail-name]').forEach(row => {
    const open = () => {
      scrollPosition = scrollY;
      title.textContent = row.dataset.detailName;
      location.hash = 'detail';
    };
    row.tabIndex = 0;
    row.setAttribute('role', 'link');
    row.addEventListener('click', event => {
      if (event.target.closest('a,button,input,select,textarea')) return;
      open();
    });
    row.addEventListener('keydown', event => {
      if (event.key !== 'Enter' && event.key !== ' ') return;
      event.preventDefault();
      open();
    });
    row.querySelector('.detail-link')?.addEventListener('click', () => {
      scrollPosition = scrollY;
      title.textContent = row.dataset.detailName;
    });
  });
  const menu = document.querySelector('.mobile-menu');
  const rail = document.querySelector('.rail');
  menu?.addEventListener('click', () => rail?.classList.toggle('mobile-open'));
  document.querySelectorAll('[data-edit-confirm]').forEach(button => button.addEventListener('click', () => {
    const reason = document.querySelector('[data-edit-reason]');
    const error = document.querySelector('[data-edit-error]');
    if (reason && !reason.value.trim()) {
      if (error) error.hidden = false;
      reason.focus();
      return;
    }
    if (error) error.hidden = true;
    let dialog = document.getElementById('edit-confirm-dialog');
    if (!dialog) {
      dialog = document.createElement('div');
      dialog.id = 'edit-confirm-dialog';
      dialog.className = 'modal-layer';
      dialog.innerHTML = '<section class="modal-card" role="dialog" aria-modal="true"><h2>変更内容を保存しますか？</h2><p>訂正理由と変更前後を監査履歴へ保存します。</p><div class="form-actions"><button class="btn" type="button" data-close-edit-confirm>戻る</button><button class="btn primary" type="button" data-save-edit>更新する</button></div></section>';
      document.body.append(dialog);
      dialog.querySelector('[data-close-edit-confirm]').addEventListener('click', () => { dialog.hidden = true; });
      dialog.querySelector('[data-save-edit]').addEventListener('click', () => { location.hash = 'detail'; });
    }
    dialog.hidden = false;
  }));
  addEventListener('hashchange', render);
  render();
})();
