import type { ComponentType } from 'react';
import {
  siBattledotnet,
  siBrave,
  siClaude,
  siDiscord,
  siEa,
  siEpicgames,
  siFirefoxbrowser,
  siNvidia,
  siObsstudio,
  siRockstargames,
  siSpotify,
  siSteam,
  siTelegram,
  siTwitch,
  siUbisoft,
  siVlcmediaplayer,
  siWhatsapp,
  type SimpleIcon,
} from 'simple-icons';
import {
  Bookmark,
  Camera,
  Columns2,
  Copy,
  EyeOff,
  Film,
  GitCommitHorizontal,
  Highlighter,
  Palette,
  Rocket,
  Save,
  Scissors,
  SkipBack,
  SkipForward,
  Split,
  ToggleLeft,
  Undo2,
  Redo2,
  ThumbsUp,
  ThumbsDown,
  ListTree,
  AppWindow,
  Aperture,
  Chrome,
  CircleDot,
  Clapperboard,
  Folder,
  Gamepad2,
  House,
  LayoutGrid,
  Lock,
  Mic,
  MicOff,
  Monitor,
  MonitorPlay,
  Pause,
  Play,
  Power,
  PowerOff,
  Radio,
  Settings,
  Square,
  Volume1,
  Volume2,
  VolumeX,
} from 'lucide-react';

/** Lo que aceptan los iconos de Lucide y los de marca. */
export type IconComponent = ComponentType<{ className?: string; size?: number | string; strokeWidth?: number | string; 'aria-hidden'?: boolean | 'true' | 'false' }>;

/** Logotipo de marca (Simple Icons, CC0; ADR-027): relleno con el color del texto, como el resto de iconos. */
function brand(icon: SimpleIcon): IconComponent {
  function BrandIcon({ className, size }: { className?: string; size?: number | string }) {
    return (
      <svg viewBox="0 0 24 24" width={size ?? 24} height={size ?? 24} className={className} fill="currentColor" aria-hidden role="img">
        {/* Un poco más pequeño que los de trazo para que pesen igual a la vista. */}
        <path d={icon.path} transform="translate(2 2) scale(0.8333)" />
      </svg>
    );
  }
  BrandIcon.displayName = `Brand(${icon.title})`;
  return BrandIcon;
}

const ICONS: Record<string, IconComponent> = {
  power: Power,
  'power-off': PowerOff,
  monitor: Monitor,
  settings: Settings,
  obs: Aperture,
  record: CircleDot,
  pause: Pause,
  play: Play,
  stop: Square,
  radio: Radio,
  chrome: Chrome,
  davinci: Clapperboard,
  mic: Mic,
  'mic-off': MicOff,
  gamepad: Gamepad2,
  'node-serial': GitCommitHorizontal,
  'node-parallel': Split,
  toggle: ToggleLeft,
  'eye-off': EyeOff,
  camera: Camera,
  columns: Columns2,
  copy: Copy,
  highlighter: Highlighter,
  'skip-back': SkipBack,
  'skip-forward': SkipForward,
  save: Save,
  film: Film,
  palette: Palette,
  rocket: Rocket,
  scissors: Scissors,
  bookmark: Bookmark,
  undo: Undo2,
  redo: Redo2,
  'thumbs-up': ThumbsUp,
  'thumbs-down': ThumbsDown,
  sections: ListTree,
  'brand-steam': brand(siSteam),
  'brand-discord': brand(siDiscord),
  'brand-claude': brand(siClaude),
  'brand-obs': brand(siObsstudio),
  'brand-spotify': brand(siSpotify),
  'brand-epic': brand(siEpicgames),
  'brand-battlenet': brand(siBattledotnet),
  'brand-rockstar': brand(siRockstargames),
  'brand-ubisoft': brand(siUbisoft),
  'brand-ea': brand(siEa),
  'brand-twitch': brand(siTwitch),
  'brand-vlc': brand(siVlcmediaplayer),
  'brand-firefox': brand(siFirefoxbrowser),
  'brand-brave': brand(siBrave),
  'brand-whatsapp': brand(siWhatsapp),
  'brand-telegram': brand(siTelegram),
  'brand-nvidia': brand(siNvidia),
  scene: MonitorPlay,
  folder: Folder,
  lock: Lock,
  home: House,
  grid: LayoutGrid,
  volume: Volume2,
  'volume-low': Volume1,
  'volume-off': VolumeX,
};

export function iconFor(name: string): IconComponent {
  return ICONS[name] ?? AppWindow;
}

export const ICON_NAMES = Object.keys(ICONS);
