(() => {
  const post = chrome.webview.postMessage.bind(chrome.webview);
  addEventListener('wheel', event => {
    if (!event.isTrusted || !event.ctrlKey || !event.deltaY || Math.abs(event.deltaX) > Math.abs(event.deltaY)) return;
    // Wait for canvas/editor handlers, including handlers registered on window, to cancel the gesture.
    setTimeout(() => {
      if (!event.defaultPrevented) post('sol.wheel:__SOL_WHEEL_KEY__:' + (event.deltaY < 0 ? 'in' : 'out'));
    }, 0);
  }, { capture: true, passive: true });
})();
