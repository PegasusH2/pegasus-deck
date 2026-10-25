/** @type {import('tailwindcss').Config} */
// Tailwind solo referencia los tokens de src/ui/tokens.css (sin valores propios).
const token = (name) => `rgb(var(--pd-${name}) / <alpha-value>)`;
const tiles = ['green', 'red', 'blue', 'violet', 'purple', 'teal', 'amber', 'slate', 'pink', 'cyan'];

export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        bg: token('bg'),
        surface: { 1: token('surface-1'), 2: token('surface-2'), 3: token('surface-3') },
        line: token('border'),
        fg: { DEFAULT: token('text'), 2: token('text-2'), 3: token('text-3') },
        ok: token('ok'),
        danger: token('danger'),
        warn: token('warn'),
        info: token('info'),
        accent: token('accent'),
        tile: Object.fromEntries(tiles.map((t) => [t, token(`tile-${t}`)])),
      },
      borderRadius: {
        sm: 'var(--pd-radius-sm)',
        md: 'var(--pd-radius-md)',
        lg: 'var(--pd-radius-lg)',
        xl: 'var(--pd-radius-xl)',
      },
      boxShadow: { pd: 'var(--pd-shadow)' },
      transitionDuration: { press: 'var(--pd-dur-press)', panel: 'var(--pd-dur-panel)' },
      spacing: { 'grid-gap': 'var(--pd-grid-gap)' },
      fontFamily: {
        sans: ['-apple-system', 'BlinkMacSystemFont', '"SF Pro Text"', '"Segoe UI"', 'Roboto', 'sans-serif'],
      },
      keyframes: {
        shake: {
          '0%, 100%': { transform: 'translateX(0)' },
          '25%': { transform: 'translateX(-4px)' },
          '75%': { transform: 'translateX(4px)' },
        },
      },
      animation: { shake: 'shake 240ms ease-out 2' },
    },
  },
  // Clases de color de botón generadas dinámicamente (bg-tile-<color>).
  safelist: tiles.map((t) => `bg-tile-${t}`),
  plugins: [],
};
