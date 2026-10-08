(() => {
  let timer, video, pointer;
  let hidden = [];
  const watched = new WeakSet();
  const restore = () => {
    clearTimeout(timer);
    for (const [el, value, priority] of hidden)
      if (el.style.getPropertyValue('cursor') === 'none' && el.style.getPropertyPriority('cursor') === 'important')
        value ? el.style.setProperty('cursor', value, priority) : el.style.removeProperty('cursor');
    hidden = [];
  };
  const playing = () => video?.isConnected && !video.paused && !video.ended && video.readyState >= 2;
  const move = event => {
    restore();
    if (event.pointerType !== 'mouse') return;
    const path = event.composedPath();
    pointer = {pointerType:'mouse', clientX:event.clientX, clientY:event.clientY, composedPath:() => path};
    if (path.some(el => el.matches?.('button,a,input,textarea,select,[role="button"],[role="slider"],[contenteditable="true"]'))) return;
    const roots = new Set([document, ...path.map(el => el.shadowRoot).filter(Boolean)]);
    video = [...roots].flatMap(root => [...root.querySelectorAll('video')]).find(el => {
      const r = el.getBoundingClientRect();
      return event.clientX >= r.left && event.clientX <= r.right && event.clientY >= r.top && event.clientY <= r.bottom;
    });
    if (video && !watched.has(video)) {
      watched.add(video);
      for (const name of ['pause','ended','emptied']) video.addEventListener(name, restore);
      video.addEventListener('play', () => { if (pointer) move(pointer); });
    }
    if (!playing()) return;
    timer = setTimeout(() => {
      if (!playing() || document.hidden) return;
      // CSSOM properties work under strict site CSP and preserve custom cursors.
      hidden = [...new Set([...path, video])].filter(el => el.style).map(el =>
        [el, el.style.getPropertyValue('cursor'), el.style.getPropertyPriority('cursor')]);
      for (const [el] of hidden) el.style.setProperty('cursor', 'none', 'important');
    }, 3000);
  };
  document.addEventListener('pointermove', move, {capture:true, passive:true});
  document.addEventListener('pointerup', move, {capture:true, passive:true});
  for (const name of ['pointerdown','keydown','wheel','pause','ended','emptied','visibilitychange','fullscreenchange'])
    document.addEventListener(name, restore, {capture:true, passive:true});
  document.addEventListener('pointerout', event => { if (!event.relatedTarget) { pointer = null; restore(); } }, true);
  window.addEventListener('blur', () => { pointer = null; restore(); });
})();
