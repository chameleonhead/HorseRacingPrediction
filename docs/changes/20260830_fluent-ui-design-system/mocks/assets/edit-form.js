(() => {
  document.querySelectorAll('[data-edit-confirm]').forEach(button => {
    button.addEventListener('click', () => {
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
    });
  });
  const dialog = document.getElementById('edit-confirm-dialog');
  document.querySelectorAll('[data-close-edit-confirm]').forEach(button => button.addEventListener('click', () => { if (dialog) dialog.hidden = true; }));
  document.querySelector('[data-save-edit]')?.addEventListener('click', () => { location.hash = 'detail'; });
  addEventListener('keydown', event => { if (event.key === 'Escape' && dialog && !dialog.hidden) dialog.hidden = true; });
})();
