(() => {
  const root = document.documentElement
  const themeToggle = document.getElementById('lc-theme-toggle')
  const contentSizeToggle = document.getElementById('lc-text-size-toggle')
  const themes = new Set(['light', 'dark', 'auto'])
  const contentSizes = new Set(['compact', 'default', 'large', 'x-large'])
  const themeOrder = ['auto', 'light', 'dark']
  const contentSizeOrder = ['default', 'large', 'x-large', 'compact']

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
    if (themeToggle) {
      const labels = { auto: 'system', light: 'light', dark: 'dark' }
      const icons = { auto: '🌓', light: '☀️', dark: '🌙' }
      themeToggle.textContent = icons[theme]
      themeToggle.title = `Theme: ${labels[theme]}`
      themeToggle.setAttribute('aria-label', `Change colour theme. Current theme: ${labels[theme]}`)
    }
  }

  const applyContentSize = (value, persist) => {
    const contentSize = contentSizes.has(value) ? value : 'default'
    if (persist) writePreference('content-size', contentSize)
    root.dataset.contentSize = contentSize
    if (contentSizeToggle) {
      const labels = { compact: 'compact', default: 'default', large: 'large', 'x-large': 'extra large' }
      contentSizeToggle.dataset.size = contentSize
      contentSizeToggle.title = `Text size: ${labels[contentSize]}`
      contentSizeToggle.setAttribute('aria-label', `Change text size. Current size: ${labels[contentSize]}`)
    }
  }

  if (themeToggle) {
    themeToggle.addEventListener('click', () => {
      const current = readPreference('theme', 'auto')
      applyTheme(themeOrder[(themeOrder.indexOf(current) + 1) % themeOrder.length], true)
    })
  }

  if (contentSizeToggle) {
    contentSizeToggle.addEventListener('click', () => {
      const current = readPreference('content-size', 'default')
      applyContentSize(contentSizeOrder[(contentSizeOrder.indexOf(current) + 1) % contentSizeOrder.length], true)
    })
  }

  applyTheme(readPreference('theme', 'auto'), false)
  applyContentSize(readPreference('content-size', 'default'), false)

  const colourScheme = window.matchMedia('(prefers-color-scheme: dark)')
  const refreshAutomaticTheme = () => {
    if (readPreference('theme', 'auto') === 'auto') applyTheme('auto', false)
  }
  if (typeof colourScheme.addEventListener === 'function') {
    colourScheme.addEventListener('change', refreshAutomaticTheme)
  } else if (typeof colourScheme.addListener === 'function') {
    colourScheme.addListener(refreshAutomaticTheme)
  }
})()
