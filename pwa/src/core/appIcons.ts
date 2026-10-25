// Icono de un acceso a app de General según la app (ADR-027): logotipo de la marca si es
// conocida (Steam, Discord, OBS…), un mando si es un juego (por su carpeta de instalación,
// que detecta el agente, o por su nombre) y, si no, un icono genérico.

export interface AppIdentity {
  id: string;
  name: string;
  /** 'game' si el agente la ha encontrado en una carpeta de juegos (Steam, Epic, Battle.net…). */
  kind?: 'game';
}

/** Palabras del nombre → icono (el primero que coincide). Los lanzadores van antes que los juegos. */
const BRANDS: readonly [RegExp, string][] = [
  [/\bsteam\b/i, 'brand-steam'],
  [/\bdiscord\b/i, 'brand-discord'],
  [/\bclaude\b/i, 'brand-claude'],
  [/\bobs\b/i, 'brand-obs'],
  [/\bspotify\b/i, 'brand-spotify'],
  [/\bepic games\b/i, 'brand-epic'],
  [/battle\.?net/i, 'brand-battlenet'],
  [/rockstar/i, 'brand-rockstar'],
  [/\bubisoft\b/i, 'brand-ubisoft'],
  [/^ea( app)?$|\bea desktop\b/i, 'brand-ea'],
  [/\btwitch\b/i, 'brand-twitch'],
  [/\bvlc\b/i, 'brand-vlc'],
  [/\bfirefox\b/i, 'brand-firefox'],
  [/\bbrave\b/i, 'brand-brave'],
  [/\bchrome\b/i, 'chrome'],
  [/\bwhatsapp\b/i, 'brand-whatsapp'],
  [/\btelegram\b/i, 'brand-telegram'],
  [/\bnvidia\b/i, 'brand-nvidia'],
  [/davinci/i, 'davinci'],
  [/explorador|explorer/i, 'folder'],
];

const GAMES = /call of duty|\bcod\b|warzone|\bgta\b|grand theft auto|red dead|fortnite|minecraft|valorant|league of legends|counter-strike|\bcs2\b|apex legends|rocket league|\bfifa\b|ea sports fc|battlefield|overwatch|elden ring|cyberpunk|witcher|diablo|rainbow six|assassin'?s creed|forza|\bhalo\b|roblox|genshin|helldivers|baldur'?s gate|starfield|destiny/i;

const BY_ID: Record<string, string> = { obs: 'brand-obs', chrome: 'chrome', davinci: 'davinci', explorer: 'folder' };

export function appIconName(app: AppIdentity): string {
  for (const [re, icon] of BRANDS) if (re.test(app.name)) return icon;
  if (app.kind === 'game' || GAMES.test(app.name)) return 'gamepad';
  return BY_ID[app.id] ?? 'app';
}
