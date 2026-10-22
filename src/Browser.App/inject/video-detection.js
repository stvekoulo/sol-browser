(() => {
  if (!window.chrome?.webview || !/^https?:$/.test(location.protocol)) return;
  const seen = new WeakMap();
  let timer;
  const report = video => {
    if (!(video instanceof HTMLVideoElement)) return;
    const src = video.currentSrc || video.src || video.querySelector('source')?.src || '';
    const protectedMedia = !!video.mediaKeys;
    const signature = `${src}|${protectedMedia}`;
    if (seen.get(video) === signature) return;
    seen.set(video, signature);
    chrome.webview.postMessage({ solVideo: true, src: src.slice(0, 16000), protected: protectedMedia });
  };
  const scan = () => document.querySelectorAll('video').forEach(report);
  const queue = () => { clearTimeout(timer); timer = setTimeout(scan, 350); };
  for (const type of ['loadedmetadata', 'play', 'emptied']) document.addEventListener(type, e => report(e.target), true);
  document.addEventListener('encrypted', () => chrome.webview.postMessage({ solVideo: true, protected: true }), true);
  const start = () => {
    scan();
    new MutationObserver(queue).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['src'] });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
})();
