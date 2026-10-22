(() => {
  if (!window.chrome?.webview || !/^https?:$/.test(location.protocol)) return;
  let seen = new WeakMap(), timer, signature = '', settle = [];
  const post = value => chrome.webview.postMessage({ solVideo: true, page: location.href, ...value });
  const youtube = /(^|\.)youtube(?:-nocookie)?\.com$/.test(location.hostname);
  const metadata = () => {
    try {
      const player = document.getElementById('movie_player');
      const response = player?.getPlayerResponse?.() || window.ytInitialPlayerResponse;
      const requested = new URL(location.href).searchParams.get('v');
      if (requested && response?.videoDetails?.videoId && requested !== response.videoDetails.videoId) return;
      const data = response?.streamingData;
      const formats = [...(data?.formats || []), ...(data?.adaptiveFormats || [])].slice(0, 90).map(f => ({
        id: String(f.itag), url: typeof f.url === 'string' ? f.url.slice(0, 16000) : '',
        height: Math.max(0, Number(f.height) || 0),
        mime: String(f.mimeType || '').slice(0, 150) + (f.height && !f.audioQuality && !f.audioChannels ? ';video-only' : ''),
        audio: !f.height && /^audio\//.test(f.mimeType || ''),
        resolve: youtube || !!f.signatureCipher || !!f.cipher,
      }));
      if (!formats.length && youtube) {
        const heights = { tiny:144, small:240, medium:360, large:480, hd720:720, hd1080:1080, hd1440:1440, hd2160:2160, highres:4320 };
        for (const q of player?.getAvailableQualityLevels?.() || []) if (heights[q])
          formats.push({ id:q, url:'', height:heights[q], mime:'video-only', audio:false, resolve:true });
      }
      if (!formats.length) return;
      const next = JSON.stringify(formats);
      if (next === signature || next.length > 350000) return;
      signature = next; post({ formats, src:'', protected:false });
    } catch {}
  };
  const report = video => {
    if (!(video instanceof HTMLVideoElement)) return;
    const src = video.currentSrc || video.src || video.querySelector('source')?.src || '';
    const protectedMedia = !!video.mediaKeys;
    const key = `${src}|${protectedMedia}|${video.videoHeight}`;
    if (seen.get(video) === key) return;
    seen.set(video, key);
    post({ src:src.slice(0,16000), height:video.videoHeight, protected:protectedMedia });
  };
  const scan = () => { document.querySelectorAll('video').forEach(report); metadata(); };
  const queue = () => { clearTimeout(timer); timer = setTimeout(scan, 80); };
  const ready = () => { settle.forEach(clearTimeout); scan(); settle=[250,750,1500,3000].map(ms=>setTimeout(metadata,ms)); };
  for (const type of ['loadedmetadata', 'play', 'emptied']) document.addEventListener(type, e => { report(e.target); queue(); }, true);
  document.addEventListener('encrypted', () => post({protected:true}), true);
  for (const type of ['yt-navigate-finish','yt-player-updated']) document.addEventListener(type,()=>{ seen=new WeakMap(); signature=''; ready(); });
  const start = () => {
    ready();
    new MutationObserver(records=>{if(records.some(r=>r.type==='attributes'||[...r.addedNodes,...r.removedNodes].some(n=>n.nodeType===1&&n.nodeName!=='SOL-FAN')))queue()}).observe(document.documentElement, { subtree: true, childList: true, attributes: true, attributeFilter: ['src'] });
  };
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', start, { once: true });
  else start();
  addEventListener('pagehide',()=>{clearTimeout(timer);settle.forEach(clearTimeout);});
})();
