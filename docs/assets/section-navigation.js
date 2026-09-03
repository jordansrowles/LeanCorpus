(() => {
  'use strict'

  const body = document.body
  const tree = document.getElementById('lcToc')
  const heading = document.getElementById('lcSectionHeading')
  if (!body || !tree || !heading) return

  const currentUrl = new URL(window.location.href)
  const rootTocUrl = new URL(body.dataset.rootToc, currentUrl)
  const apiRoot = new URL(body.dataset.apiRoot, currentUrl)
  const apiTocUrl = new URL(body.dataset.apiToc, currentUrl)
  const isApiPage = currentUrl.pathname.startsWith(apiRoot.pathname)

  fetch(isApiPage ? apiTocUrl : rootTocUrl)
    .then(response => {
      if (!response.ok) throw new Error(`Navigation request failed with ${response.status}`)
      return response.json()
    })
    .then(model => {
      if (isApiPage) {
        renderSection({ name: 'API', href: 'index.html', items: model.items || [] }, apiTocUrl)
        return
      }

      const items = model.items || []
      const section = findCurrentSection(items, rootTocUrl) || items.find(item => item.name === 'About')
      if (!section) throw new Error('No documentation section was found.')
      renderSection(section, rootTocUrl)
    })
    .catch(() => {
      tree.replaceChildren(message('Section navigation could not be loaded.'))
    })

  function renderSection(section, tocUrl) {
    markCurrentBranch(section.items || [], tocUrl)

    const link = document.createElement('a')
    link.textContent = section.name
    link.href = section.href ? new URL(section.href, tocUrl).href : '#'
    heading.replaceChildren(link)

    tree.replaceChildren(createList(section.items || [], tocUrl))
    document.querySelector(`.lc-topnav-links [data-section="${CSS.escape(section.name)}"]`)
      ?.setAttribute('aria-current', 'page')
    tree.querySelector('li.active')?.scrollIntoView({ block: 'nearest' })
  }

  function findCurrentSection(items, tocUrl) {
    for (const item of items) {
      if (containsCurrentPage(item, tocUrl)) return item
    }
    return null
  }

  function containsCurrentPage(item, tocUrl) {
    if (item.href && matchesCurrentPage(new URL(item.href, tocUrl))) return true
    return Array.isArray(item.items) && item.items.some(child => containsCurrentPage(child, tocUrl))
  }

  function markCurrentBranch(items, tocUrl) {
    let containsCurrent = false
    for (const item of items) {
      const childContainsCurrent = item.items ? markCurrentBranch(item.items, tocUrl) : false
      const itemIsCurrent = item.href ? matchesCurrentPage(new URL(item.href, tocUrl)) : false
      item.current = itemIsCurrent
      item.expanded = itemIsCurrent || childContainsCurrent
      containsCurrent ||= item.expanded
    }
    return containsCurrent
  }

  function matchesCurrentPage(itemUrl) {
    const itemPath = normalisePath(itemUrl.pathname)
    if (currentPagePaths(currentUrl.pathname).includes(itemPath)) return true
    if (!itemUrl.pathname.endsWith('.html')) return false
    const typePagePrefix = itemUrl.pathname.slice(0, -5) + '.'
    return currentUrl.pathname.startsWith(typePagePrefix)
  }

  function currentPagePaths(path) {
    if (path.endsWith('/')) return [`${path}index.html`]
    if (path.endsWith('.html')) return [path]
    return [`${path}.html`, `${path}/index.html`]
  }

  function normalisePath(path) {
    return path.endsWith('/') ? `${path}index.html` : path
  }

  function createList(items, tocUrl) {
    const list = document.createElement('ul')
    list.className = 'nav'
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
        toggle.setAttribute('tabindex', '0')
        toggle.setAttribute('aria-label', `Toggle ${item.name}`)
        toggle.setAttribute('aria-expanded', String(Boolean(item.expanded)))
        const toggleBranch = () => {
          const expanded = listItem.classList.toggle('expanded')
          if (expanded && !childList) {
            childList = createList(item.items, tocUrl)
            listItem.append(childList)
          }
          toggle.setAttribute('aria-expanded', String(expanded))
        }
        toggle.addEventListener('click', toggleBranch)
        toggle.addEventListener('keydown', event => {
          if (event.key === 'Enter' || event.key === ' ') {
            event.preventDefault()
            toggleBranch()
          }
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
        label.className = 'name-only'
        label.textContent = item.name
        listItem.append(label)
      }

      if (hasChildren && item.expanded) {
        childList = createList(item.items, tocUrl)
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
