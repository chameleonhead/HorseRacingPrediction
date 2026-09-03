(() => {
  const modal = document.getElementById('reacquire-dialog');
  const open = document.getElementById('open-reacquire');
  const close = document.getElementById('close-reacquire');
  const confirm = document.getElementById('confirm-reacquire');
  const notice = document.getElementById('reacquire-notice');
  const setOpen = value => { if (modal) modal.hidden = !value; };
  open?.addEventListener('click', () => { setOpen(true); close?.focus(); });
  close?.addEventListener('click', () => { setOpen(false); open?.focus(); });
  confirm?.addEventListener('click', () => {
    setOpen(false);
    if (notice) {
      notice.textContent = '再取得の収集ジョブを投入しました。最新の収集ジョブから進行状況を確認できます。';
      notice.hidden = false;
    }
    open?.focus();
  });
  modal?.addEventListener('click', event => {
    if (event.target !== modal) return;
    setOpen(false);
    open?.focus();
  });
  addEventListener('keydown', event => {
    if (event.key === 'Escape' && modal && !modal.hidden) {
      setOpen(false);
      open?.focus();
    }
  });
})();
