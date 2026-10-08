(() => {
  let timer, video, pointer;
  const hidden = new Map();
  const restore = () => {
    clearTimeout(timer);
    for (const [el, [value, priority]] of hidden)
      if (el.style.getPropertyValue('cursor') === 'none' && el.style.getPropertyPriority('cursor') === 'important')
        value ? el.style.setProperty('cursor', value, priority) : el.style.removeProperty('cursor');
    hidden.clear();
  };
  const playing = () => video?.isConnected && !video.paused && !video.ended && video.readyState >= 2;
  const surface = () => {
    if (!pointer) return [];
    const path = [];
    let el = document.elementFromPoint(pointer.x, pointer.y);
    while (el?.shadowRoot?.elementFromPoint(pointer.x, pointer.y) && el.shadowRoot.elementFromPoint(pointer.x, pointer.y) !== el)
      el = el.shadowRoot.elementFromPoint(pointer.x, pointer.y);
    for (; el; el = el.parentElement || el.getRootNode().host) path.push(el);
    const roots = new Set([document, ...path.map(el => el.shadowRoot).filter(Boolean)]);
    video = [...roots].flatMap(root => [...root.querySelectorAll('video')]).find(el => {
      const r = el.getBoundingClientRect();
      return !el.paused && !el.ended && r.width >= 120 && r.height >= 60 && pointer.x >= r.left && pointer.x <= r.right && pointer.y >= r.top && pointer.y <= r.bottom;
    });
    if (!playing()) return [];
    const r = video.getBoundingClientRect();
    if (path.some(el => {
      if (el.matches('a,input,textarea,select,[role="slider"],[contenteditable="true"]')) return true;
      if (!el.matches('button,[role="button"]')) return false;
      const box = el.getBoundingClientRect();
      // Une grande surface de lecture cliquable n'est pas un bouton de commande.
      return box.width < r.width * .8 || box.height < r.height * .8;
    })) return [];
    return [...new Set([...path, video])];
  };
  const hide = () => {
    const path = document.hidden ? [] : surface();
    if (!path.length) { restore(); return; }
    for (const el of path) {
      if (!hidden.has(el)) hidden.set(el, [el.style.getPropertyValue('cursor'), el.style.getPropertyPriority('cursor')]);
      if (el.style.getPropertyValue('cursor') !== 'none' || el.style.getPropertyPriority('cursor') !== 'important')
        el.style.setProperty('cursor', 'none', 'important');
    }
    // Les lecteurs remplacent leurs calques après le fondu des commandes.
    timer = setTimeout(hide, 300);
  };
  const arm = () => { restore(); if (surface().length) timer = setTimeout(hide, 3000); };
  const move = event => {
    if (event.pointerType !== 'mouse') { pointer = null; restore(); return; }
    if (pointer?.x === event.clientX && pointer?.y === event.clientY) return;
    pointer = {x:event.clientX, y:event.clientY};
    arm();
  };
  document.addEventListener('pointermove', move, {capture:true, passive:true});
  document.addEventListener('pointerup', event => { pointer = null; move(event); }, {capture:true, passive:true});
  for (const name of ['pointerdown','keydown','wheel','pause','ended','emptied'])
    document.addEventListener(name, restore, {capture:true, passive:true});
  for (const name of ['play','playing','fullscreenchange','visibilitychange'])
    document.addEventListener(name, arm, {capture:true, passive:true});
  document.addEventListener('pointerleave', () => { pointer = null; restore(); });
  window.addEventListener('blur', () => { pointer = null; restore(); });
})();
