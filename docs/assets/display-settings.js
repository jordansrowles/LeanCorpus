(() => {
  const root = document.documentElement
  const themeSelect = document.getElementById('lc-theme-select')
  const contentSizeSelect = document.getElementById('lc-content-size-select')
  const themes = new Set(['light', 'dark', 'auto'])
  const contentSizes = new Set(['compact', 'default', 'large', 'x-large'])

  const readPreference = (key, fallback) => {
    try {
      const value = localStorage.getItem(key)
      return value && value.length > 0 ? value : fallback
    } catch {
      return fallback
    }
  }

  const writePreference = (key, value) => {
    try {
      localStorage.setItem(key, value)
    } catch {
      // Preferences remain available for the current page when storage is disabled.
    }
  }

  const applyTheme = (value, persist) => {
    const theme = themes.has(value) ? value : 'auto'
    if (persist) writePreference('theme', theme)
    root.setAttribute(
      'data-bs-theme',
      theme === 'auto'
        ? (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light')
        : theme)
    if (themeSelect && themeSelect.value !== theme) themeSelect.value = theme
  }

  const applyContentSize = (value, persist) => {
    const contentSize = contentSizes.has(value) ? value : 'default'
    if (persist) writePreference('content-size', contentSize)
    root.dataset.contentSize = contentSize
    if (contentSizeSelect && contentSizeSelect.value !== contentSize) contentSizeSelect.value = contentSize
  }

  if (themeSelect) {
    themeSelect.value = readPreference('theme', 'auto')
    themeSelect.addEventListener('change', () => applyTheme(themeSelect.value, true))
  }

  if (contentSizeSelect) {
    contentSizeSelect.value = readPreference('content-size', 'default')
    contentSizeSelect.addEventListener('change', () => applyContentSize(contentSizeSelect.value, true))
  }

  applyTheme(readPreference('theme', 'auto'), false)
  applyContentSize(readPreference('content-size', 'default'), false)

  const colourScheme = window.matchMedia('(prefers-color-scheme: dark)')
  const refreshAutomaticTheme = () => {
    if (themeSelect?.value === 'auto') applyTheme('auto', false)
  }
  if (typeof colourScheme.addEventListener === 'function') {
    colourScheme.addEventListener('change', refreshAutomaticTheme)
  } else if (typeof colourScheme.addListener === 'function') {
    colourScheme.addListener(refreshAutomaticTheme)
  }
})()
