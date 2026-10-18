import { useEffect, useState } from 'react';

export function useIconLook(src: string | null) {
  const [look, setLook] = useState<{ tone: string; color: string | null }>({ tone: '', color: null });
  useEffect(() => {
    setLook({ tone: '', color: null });
    if (!src) return;
    const img = new Image();
    img.onload = () => {
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = 16;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      ctx.drawImage(img, 0, 0, 16, 16);
      const px = ctx.getImageData(0, 0, 16, 16).data;
      let light = 0, count = 0, vivid = 0;
      const sum = [0, 0, 0];
      for (let i = 0; i < px.length; i += 4) {
        if (px[i + 3] < 128) continue; // pixels transparents ignorés
        const [r, g, b] = [px[i], px[i + 1], px[i + 2]];
        light += 0.299 * r + 0.587 * g + 0.114 * b;
        count++;
        const max = Math.max(r, g, b);
        if (max > 60 && (max - Math.min(r, g, b)) / max > 0.35) {
          sum[0] += r; sum[1] += g; sum[2] += b;
          vivid++;
        }
      }
      const mean = count ? light / count : 128;
      setLook({
        tone: vivid > count * 0.08 ? '' : mean < 60 ? ' tone-dark' : mean > 225 ? ' tone-light' : '',
        color: vivid > count * 0.08 ? `rgb(${sum.map(v => Math.round(v / vivid)).join(', ')})` : null,
      });
    };
    img.src = src;
    return () => void (img.onload = null);
  }, [src]);
  return look;
}
