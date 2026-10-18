import {Button} from '@/components/motion/button/base';
import { useState, type CSSProperties, type RefObject } from 'react';
import { send } from './bridge';
import { t } from './i18n';

type Group = { create: boolean; id: string; name: string; color: string; icon: string; presets: string[] };

// Icônes proposées ; le champ libre accepte n'importe quel autre emoji.
const ICONS = ['💼', '📚', '💻', '🛠️', '🧪', '📝', '📰', '💬', '🎬', '🎵', '🎮', '🎨', '📷', '🛒', '💰', '✈️', '🏠', '🍔', '⚽', '🎓', '🏥', '🔒', '💡', '⭐', '❤️', '🔥', '🌍', '📅'];

// Couleur ↔ teinte, saturation, clarté : les trois curseurs du sélecteur.
function toHsl(hex: string): [number, number, number] {
  const [r, g, b] = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255);
  const max = Math.max(r, g, b), min = Math.min(r, g, b), l = (max + min) / 2, d = max - min;
  if (d === 0) return [0, 0, Math.round(l * 100)];
  const s = d / (1 - Math.abs(2 * l - 1));
  const h = max === r ? ((g - b) / d + 6) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
  return [Math.round(h * 60), Math.round(s * 100), Math.round(l * 100)];
}

function toHex(h: number, s: number, l: number): string {
  const a = (s / 100) * Math.min(l / 100, 1 - l / 100);
  const channel = (n: number) => {
    const k = (n + h / 30) % 12;
    return Math.round(255 * (l / 100 - a * Math.max(-1, Math.min(k - 3, 9 - k, 1)))).toString(16).padStart(2, '0');
  };
  return `#${channel(0)}${channel(8)}${channel(4)}`;
}

const isHex = (text: string) => /^#[0-9a-fA-F]{6}$/.test(text);

// Création ou modification d'un groupe d'onglets : nom, couleur libre, icône.
export default function GroupEditor({ rootRef, group }: { rootRef: RefObject<HTMLDivElement | null>; group: Group }) {
  const [name, setName] = useState(group.name);
  const [color, setColor] = useState(group.color);
  const [hexText, setHexText] = useState(group.color);
  const [icon, setIcon] = useState(group.icon);
  const [h, s, l] = toHsl(color);

  const pick = (hex: string) => {
    setColor(hex.toLowerCase());
    setHexText(hex.toLowerCase());
  };
  const slide = (hue: number, sat: number, light: number) => pick(toHex(hue, sat, light));

  return (
    <div ref={rootRef} className="popup arriving">
      <form
        className="name-form group-editor"
        style={{ '--group': color } as CSSProperties}
        onSubmit={e => {
          e.preventDefault();
          if (name.trim()) send({ type: group.create ? 'group.create' : 'group.update', id: group.id, title: name.trim(), query: color, input: icon });
        }}
      >
        <span className="name-form-title">{group.create ? t('Nouveau groupe', 'New group') : t('Modifier le groupe', 'Edit group')}</span>

        <div className="group-preview" aria-hidden>
          {icon ? <span className="group-preview-icon">{icon}</span> : <span className="group-preview-dot" />}
          <span>{name.trim() || t('Nom du groupe', 'Group name')}</span>
        </div>

        <input id="name-input" aria-label={t('Nom du groupe', 'Group name')} autoFocus maxLength={30} placeholder={t('Nom du groupe', 'Group name')} spellCheck={false} value={name} onChange={e => setName(e.target.value)} onFocus={e => e.target.select()} />

        <fieldset>
          <legend>{t('Couleur', 'Color')}</legend>
          <div className="swatches">
            {group.presets.map(preset => (
              <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} key={preset} type="button" className={`swatch${preset === color ? ' on' : ''}`} style={{ background: preset }} aria-label={t(`Couleur ${preset}`, `Color ${preset}`)} aria-pressed={preset === color} onClick={() => pick(preset)} />
            ))}
          </div>
          <label className="slider">
            <span>{t('Teinte', 'Hue')}</span>
            <input type="range" className="hue" min={0} max={359} value={h} onChange={e => slide(+e.target.value, s || 60, l)} />
          </label>
          <label className="slider">
            <span>{t('Intensité', 'Saturation')}</span>
            <input type="range" min={0} max={100} value={s} style={{ background: `linear-gradient(90deg, ${toHex(h, 0, l)}, ${toHex(h, 100, l)})` }} onChange={e => slide(h, +e.target.value, l)} />
          </label>
          <label className="slider">
            <span>{t('Clarté', 'Lightness')}</span>
            <input type="range" min={15} max={90} value={l} style={{ background: `linear-gradient(90deg, ${toHex(h, s, 15)}, ${toHex(h, s, 52)}, ${toHex(h, s, 90)})` }} onChange={e => slide(h, s, +e.target.value)} />
          </label>
          <label className="slider">
            <span>Code</span>
            <input
              type="text"
              className="hex"
              aria-label={t('Code de la couleur', 'Color code')}
              maxLength={7}
              spellCheck={false}
              value={hexText}
              onChange={e => {
                setHexText(e.target.value);
                if (isHex(e.target.value)) setColor(e.target.value.toLowerCase());
              }}
              onBlur={() => setHexText(color)}
            />
          </label>
        </fieldset>

        <fieldset>
          <legend>{t('Icône', 'Icon')}</legend>
          <div className="icons">
            <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="button" className={`icon-choice none${icon === '' ? ' on' : ''}`} aria-pressed={icon === ''} title={t('Sans icône', 'No icon')} onClick={() => setIcon('')}>
              {t('Aucune', 'None')}
            </Button>
            {ICONS.map(choice => (
              <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} key={choice} type="button" className={`icon-choice${choice === icon ? ' on' : ''}`} aria-pressed={choice === icon} aria-label={t(`Icône ${choice}`, `Icon ${choice}`)} onClick={() => setIcon(choice)}>
                {choice}
              </Button>
            ))}
          </div>
          <label className="slider">
            <span>{t('Autre', 'Other')}</span>
            <input type="text" className="hex" aria-label={t('Autre emoji', 'Other emoji')} placeholder={t('Coller un emoji (Win + .)', 'Paste an emoji (Win + .)')} maxLength={16} value={ICONS.includes(icon) ? '' : icon} onChange={e => setIcon(e.target.value.trim())} />
          </label>
        </fieldset>

        <div className="name-form-actions">
          <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="button" className="pill-action" onClick={() => send({ type: 'popup.close' })}>{t('Annuler', 'Cancel')}</Button>
          <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="submit" className="pill-action primary" disabled={!name.trim()}>{group.create ? t('Créer le groupe', 'Create group') : t('Enregistrer', 'Save')}</Button>
        </div>
      </form>
    </div>
  );
}
