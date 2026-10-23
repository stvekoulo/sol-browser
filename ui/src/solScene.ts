import logo from './assets/logo.png';
let emblem: HTMLImageElement | null = null;

// Les outils de composition utilisent aussi l’image originale, sans redessiner le logo.
export function drawSol(g:CanvasRenderingContext2D,w:number,h:number,_time:number) {
  if(!emblem) { emblem=new Image(); emblem.src=logo; }
  g.fillStyle='#000'; g.fillRect(0,0,w,h);
  if(!emblem.complete||!emblem.naturalWidth) return;
  const size=Math.min(w,h)*.82;
  g.drawImage(emblem,(w-size)/2,(h-size)/2,size,size);
}
