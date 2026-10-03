new Promise(resolve => {
  const deltas = []; let last = performance.now(); const start = last;
  const tick = now => {
    deltas.push(now - last); last = now;
    window.scrollBy(0, 20);
    if (now - start < 5000) requestAnimationFrame(tick);
    else {
      deltas.sort((a, b) => a - b);
      const p95 = deltas[Math.floor(deltas.length * 0.95)];
      const slow = deltas.filter(d => d > 25).length;
      resolve(`${deltas.length} trames, p95 ${p95.toFixed(1)} ms, ${slow} lentes`);
    }
  };
  requestAnimationFrame(tick);
});
