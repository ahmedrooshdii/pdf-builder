// Applies the saved or system theme before Angular boots, so a dark-mode user never sees a white
// flash. A separate file rather than an inline script so the CSP can keep script-src 'self'.
// ThemeService owns the theme from then on; the key must match THEME_STORAGE_KEY there.
(function () {
  var theme = null;
  try {
    theme = localStorage.getItem('pdf-builder.theme');
  } catch (e) {}
  if (theme !== 'light' && theme !== 'dark') {
    theme = matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  document.documentElement.classList.toggle('dark', theme === 'dark');
})();
