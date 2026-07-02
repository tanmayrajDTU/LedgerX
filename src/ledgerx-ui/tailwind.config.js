/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        background: '#0B0F19',
        card: '#151D30',
        border: '#1F2A45',
        primary: {
          50: '#F0F9FF',
          100: '#E0F2FE',
          500: '#3B82F6',
          600: '#2563EB',
          700: '#1D4ED8',
        },
        accent: {
          50: '#ECFDF5',
          500: '#10B981',
          600: '#059669',
        },
        warning: {
          500: '#F59E0B',
        },
        danger: {
          500: '#EF4444',
          600: '#DC2626',
        }
      }
    },
  },
  plugins: [],
}
