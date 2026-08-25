(() => {
  'use strict'

  const body = document.body
  const section = document.getElementById('apiNavigation')
  const tree = document.getElementById('apiToc')
  if (!body || !section || !tree) return

  const apiRoot = new URL(body.dataset.apiRoot, window.location.href)
  const currentUrl = new URL(window.location.href)
  const apiToggle = document.getElementById('lc-api-tree-toggle')
  const isApiPage = currentUrl.pathname.startsWith(apiRoot.pathname)
  let loaded = false
  let loading = false

  const readPreference = () => {
    try {
      const value = localStorage.getItem('show-api-tree')
      return value === 'true' ? true : value === 'false' ? false : null
    } catch {
      return null
    }
  }

  const writePreference = value => {
    try {
      localStorage.setItem('show-api-tree', String(value))
    } catch {
      // The switch remains available for the current page when storage is disabled.
    }
  }

  const tocUrl = new URL(body.dataset.apiToc, window.location.href)

  function setVisible(visible, persist) {
    if (persist) writePreference(visible)
    section.hidden = !visible
    body.classList.toggle('lc-api-tree-visible', visible)
    if (apiToggle && apiToggle.checked !== visible) apiToggle.checked = visible
    if (visible) load()
  }

  function load() {
    if (loaded || loading) return

    loading = true
    fetch(tocUrl)
      .then(response => {
        if (!response.ok) throw new Error(`API navigation request failed with ${response.status}`)
        return response.json()
      })
      .then(model => {
        render(model.items || [])
        loaded = true
      })
      .catch(() => {
        tree.replaceChildren(message('API navigation could not be loaded.'))
      })
      .finally(() => {
        loading = false
      })
  }

  const storedVisibility = readPreference()
  setVisible(storedVisibility ?? isApiPage, false)
  apiToggle?.addEventListener('change', () => setVisible(apiToggle.checked, true))

  function render(items) {
    markCurrentBranch(items)
    tree.replaceChildren(createList(items))
    tree.querySelector('li.active')?.scrollIntoView({ block: 'nearest' })
  }

  function markCurrentBranch(items) {
    let containsCurrent = false
    for (const item of items) {
      const childContainsCurrent = item.items ? markCurrentBranch(item.items) : false
      const itemIsCurrent = item.href ? matchesCurrentPage(new URL(item.href, tocUrl)) : false
      item.current = itemIsCurrent
      item.expanded = itemIsCurrent || childContainsCurrent
      containsCurrent ||= item.expanded
    }
    return containsCurrent
  }

  function matchesCurrentPage(itemUrl) {
    if (itemUrl.pathname === currentUrl.pathname) return true
    if (!itemUrl.pathname.endsWith('.html')) return false
    const typePagePrefix = itemUrl.pathname.slice(0, -5) + '.'
    return currentUrl.pathname.startsWith(typePagePrefix)
  }

  function createList(items) {
    const list = document.createElement('ul')
    for (const item of items) {
      if (!item.name) continue

      const listItem = document.createElement('li')
      const hasChildren = Array.isArray(item.items) && item.items.length > 0
      let childList = null
      if (hasChildren) listItem.classList.add('expander')
      if (item.expanded) listItem.classList.add('expanded')
      if (item.current) listItem.classList.add('active')

      if (hasChildren) {
        const toggle = document.createElement('span')
        toggle.className = 'expand-stub'
        toggle.setAttribute('role', 'button')
        toggle.setAttribute('aria-label', `Toggle ${item.name}`)
        toggle.setAttribute('aria-expanded', String(Boolean(item.expanded)))
        toggle.addEventListener('click', () => {
          const expanded = listItem.classList.toggle('expanded')
          if (expanded && !childList) {
            childList = createList(item.items)
            listItem.append(childList)
          }
          toggle.setAttribute('aria-expanded', String(expanded))
        })
        listItem.append(toggle)
      }

      if (item.href) {
        const link = document.createElement('a')
        link.href = new URL(item.href, tocUrl).href
        link.textContent = item.name
        link.className = 'nav-link'
        if (item.current) link.setAttribute('aria-current', 'page')
        listItem.append(link)
      } else {
        const label = document.createElement('span')
        label.textContent = item.name
        listItem.append(label)
      }

      if (hasChildren && item.expanded) {
        childList = createList(item.items)
        listItem.append(childList)
      }
      list.append(listItem)
    }
    return list
  }

  function message(text) {
    const element = document.createElement('p')
    element.className = 'text-secondary small'
    element.textContent = text
    return element
  }
})()
