(() => {
  const mergeModal = document.getElementById('owner-merge-modal');
  const confirmModal = document.getElementById('merge-confirm');
  const open = document.getElementById('open-owner-merge');
  const close = document.getElementById('close-owner-merge');
  const cancel = document.getElementById('cancel-owner-merge');
  const review = document.getElementById('open-merge-confirm');
  const back = document.getElementById('close-merge-confirm');
  const setOpen = (modal, value) => { if (modal) modal.hidden = !value; };
  open?.addEventListener('click', () => { setOpen(mergeModal, true); close?.focus(); });
  close?.addEventListener('click', () => { setOpen(mergeModal, false); open?.focus(); });
  cancel?.addEventListener('click', () => { setOpen(mergeModal, false); open?.focus(); });
  review?.addEventListener('click', () => { setOpen(confirmModal, true); back?.focus(); });
  back?.addEventListener('click', () => { setOpen(confirmModal, false); review?.focus(); });
  [mergeModal, confirmModal].forEach(modal => modal?.addEventListener('click', event => {
    if (event.target !== modal) return;
    setOpen(modal, false);
    (modal === mergeModal ? open : review)?.focus();
  }));
  addEventListener('keydown', event => {
    if (event.key !== 'Escape') return;
    if (confirmModal && !confirmModal.hidden) { setOpen(confirmModal, false); review?.focus(); }
    else if (mergeModal && !mergeModal.hidden) { setOpen(mergeModal, false); open?.focus(); }
  });
  if (location.hash === '#merge') {
    history.replaceState(null, '', '#detail');
    window.dispatchEvent(new HashChangeEvent('hashchange'));
    setOpen(mergeModal, true);
    close?.focus();
  }
})();
