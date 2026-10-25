import {Button} from '@/components/motion/button/base';
import { useEffect, useState, type CSSProperties, type RefObject } from 'react';
import { EyeTracker } from '@/components/bencho/EyeTracker';
import { ThinkingShimmer } from '@/components/agents/loading-states/thinking-shimmer';
import './sidebar-ai.css';
import { send } from './bridge';
import { ColorPicker, ColorPickerArea, ColorPickerHexInput, ColorPickerHue, ColorPickerPreset, ColorPickerPresets, ColorPickerSwatch } from '@/components/motion/color-picker';
import { t } from './i18n';

type Group = { create: boolean; id: string; name: string; color: string; icon: string; presets: string[]; count?: number; ai?: boolean; suggesting?: boolean; suggestion?: { name: string; color: string; icon: string } | null };

// Icônes proposées ; le champ libre accepte n'importe quel autre emoji.
const ICONS = ['💼', '📚', '💻', '🛠️', '🧪', '📝', '📰', '💬', '🎬', '🎵', '🎮', '🎨', '📷', '🛒', '💰', '✈️', '🏠', '🍔', '⚽', '🎓', '🏥', '🔒', '💡', '⭐', '❤️', '🔥', '🌍', '📅'];

// Création ou modification d'un groupe d'onglets : nom, couleur libre, icône.
export default function GroupEditor({ rootRef, group }: { rootRef: RefObject<HTMLDivElement | null>; group: Group }) {
  const [name, setName] = useState(group.name);
  const [color, setColor] = useState(group.color);
  const [icon, setIcon] = useState(group.icon);
  // La suggestion de Sol AI remplit le formulaire ; l'utilisateur garde la main avant d'enregistrer.
  useEffect(() => {
    const s = group.suggestion;
    if (!s) return;
    setName(s.name);
    if (s.color) setColor(s.color);
    if (s.icon) setIcon(s.icon);
  }, [group.suggestion]);

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
        <span className="name-form-title">{group.create ? t('Nouveau groupe', 'New group') : t('Modifier le groupe', 'Edit group')}{group.create && (group.count ?? 0) > 1 ? t(` · ${group.count} onglets`, ` · ${group.count} tabs`) : ''}</span>

        <div className="group-preview" aria-hidden>
          {icon ? <span className="group-preview-icon">{icon}</span> : <span className="group-preview-dot" />}
          <span>{name.trim() || t('Nom du groupe', 'Group name')}</span>
        </div>

        {group.ai && <div className="group-ai" data-eyt-frame>
          <EyeTracker className="eyt-inline" size={20} eyeScale={1.6} />
          {group.suggesting ? <ThinkingShimmer>{t('Sol AI regarde les onglets du groupe…', 'Sol AI is looking at the group’s tabs…')}</ThinkingShimmer>
            : <Button variant="ghost" size="sm" pressScale={0.98} whileHover={undefined} type="button" className="pill-action" onClick={() => send({ type: 'group.ai', id: group.id })}>{t('Suggérer avec Sol AI', 'Suggest with Sol AI')}</Button>}
        </div>}
        <input id="name-input" aria-label={t('Nom du groupe', 'Group name')} autoFocus maxLength={30} placeholder={t('Nom du groupe', 'Group name')} spellCheck={false} value={name} onChange={e => setName(e.target.value)} onFocus={e => e.target.select()} />

        <fieldset>
          <legend>{t('Couleur', 'Color')}</legend>
          {/* Sélecteur beUI dans la vue : zone intensité/clarté, teinte, code et couleurs proposées. */}
          <ColorPicker className="group-color" value={color} alpha={false} onValueChange={hex => setColor(hex.slice(0, 7).toLowerCase())}>
            <ColorPickerArea className="group-color-area" label={t('Intensité et clarté', 'Saturation and brightness')} />
            <ColorPickerHue label={t('Teinte', 'Hue')} />
            <div className="group-color-row">
              <ColorPickerSwatch />
              <ColorPickerHexInput aria-label={t('Code de la couleur', 'Color code')} />
            </div>
            <ColorPickerPresets>
              {group.presets.map(preset => <ColorPickerPreset key={preset} value={preset} label={t(`Couleur ${preset}`, `Color ${preset}`)} />)}
            </ColorPickerPresets>
          </ColorPicker>
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
