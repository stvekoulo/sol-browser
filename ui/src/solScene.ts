// L'animation de Sol : le soleil au-dessus de la lagune, trois vagues qui passent. Dessinée image par image sur un
// canevas, aux couleurs du logo ; la même fonction sert à l'accueil et à l'enregistrement de la vidéo (docs/media).
// « t » est le temps en secondes ; le dessin est prévu pour un cadre 16:9 et s'adapte à sa taille.
export function drawSol(g: CanvasRenderingContext2D, w: number, h: number, t: number) {
  const k = w / 960; // tout est décrit pour 960 de large
  const n = t * 0.75;

  const sky = g.createLinearGradient(0, 0, 0, h);
  sky.addColorStop(0, '#ff9a3d');
  sky.addColorStop(0.55, '#ffd36b');
  sky.addColorStop(1, '#ffe9b8');
  g.fillStyle = sky;
  g.fillRect(0, 0, w, h);

  const sunY = h * 0.555 - Math.sin(n / 3) * 12 * k;
  const sun = g.createRadialGradient(w / 2, sunY, 10 * k, w / 2, sunY, 150 * k);
  sun.addColorStop(0, '#fff6c2');
  sun.addColorStop(1, '#ffb21f');
  g.fillStyle = sun;
  g.beginPath();
  g.arc(w / 2, sunY, 130 * k, 0, Math.PI * 2);
  g.fill();

  const waves: [number, string, number, number][] = [[0.611, '#35b6ff', 1, 16], [0.685, '#1a8cff', 1.4, 20], [0.778, '#0f5fe0', 1.9, 24]];
  for (const [level, color, speed, height] of waves) {
    g.fillStyle = color;
    g.beginPath();
    g.moveTo(0, h);
    for (let x = 0; x <= w; x += 12 * k) g.lineTo(x, h * level + Math.sin(x / (110 * k) + n * speed) * height * k);
    g.lineTo(w, h * level);
    g.lineTo(w, h);
    g.fill();
  }
}
