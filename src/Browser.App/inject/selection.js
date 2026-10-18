// Barre d'actions sur la sélection : signale à Sol le texte choisi et sa position, et sa disparition.
// Rien n'est envoyé hors de l'ordinateur ; les champs de saisie gardent le menu contextuel.
(() => {
  if (window.top !== window) return;
  let shown = false;
  const post = message => window.chrome?.webview?.postMessage(message);
  const clear = () => { if (shown) { shown = false; post('sol.selection.none'); } };
  const report = () => {
    const s = getSelection();
    const text = s && !s.isCollapsed ? s.toString().trim() : '';
    const active = document.activeElement;
    if (!text || text.length > 50000 || active?.matches?.('input,textarea') || active?.isContentEditable) return clear();
    const r = s.getRangeAt(0).getBoundingClientRect();
    if (!r.width && !r.height) return clear();
    shown = true;
    post(JSON.stringify({ sol: 'selection', x: r.left + r.width / 2, top: r.top, bottom: r.bottom, ratio: devicePixelRatio,
      text, title: document.title, language: document.documentElement.lang || '' }));
  };
  document.addEventListener('mouseup', e => { if (e.isTrusted && e.button === 0) setTimeout(report, 10); }, true);
  document.addEventListener('keyup', e => { if (e.isTrusted && e.shiftKey) setTimeout(report, 10); }, true);
  document.addEventListener('selectionchange', () => { if (shown && getSelection()?.isCollapsed) clear(); });
  addEventListener('scroll', clear, { passive: true, capture: true });
})();
