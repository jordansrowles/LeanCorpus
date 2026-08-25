(() => {
  'use strict'

  const state = { index: null, indices: [] }
  const byId = id => document.getElementById(id)
  const notice = byId('notice')
  const defaultSchema = {
    fields: [{ name: 'content', type: 0, indexed: true, stored: true, multiValued: false, analyser: 'standard' }],
    analysis: {}
  }
  byId('createIndexSchema').value = JSON.stringify(defaultSchema, null, 2)

  document.querySelectorAll('[data-view]').forEach(button => button.addEventListener('click', () => show(button.dataset.view)))
  byId('createIndexForm').addEventListener('submit', createIndex)
  byId('indexDocumentForm').addEventListener('submit', indexDocument)
  byId('settingsForm').addEventListener('submit', saveSettings)
  byId('deleteIndex').addEventListener('click', deleteIndex)
  byId('runSearch').addEventListener('click', runSearch)
  byId('runExplain').addEventListener('click', runExplain)

  refreshOverview().catch(report)
  refreshIndices().catch(report)

  async function api(path, options = {}) {
    const response = await fetch(path, {
      ...options,
      headers: { 'Content-Type': 'application/json', ...(options.headers || {}) }
    })
    const result = await response.json()
    if (!response.ok || result.failure) throw new Error(result.failure?.message || `Request failed with ${response.status}`)
    return result.value
  }

  function show(view) {
    document.querySelectorAll('.view').forEach(section => { section.hidden = section.id !== view })
    document.querySelectorAll('[data-view]').forEach(button => button.classList.toggle('active', button.dataset.view === view))
    if (view === 'overview') refreshOverview().catch(report)
    if (view === 'indices') refreshIndices().catch(report)
    if (state.index) refreshIndexView(view).catch(report)
  }

  async function refreshOverview() {
    const [health, readiness, indices] = await Promise.all([
      api('/v1/health'), api('/v1/ready'), api('/v1/indices')
    ])
    const cards = byId('serverOverview')
    cards.replaceChildren(
      card('Health', health.status),
      card('Readiness', readiness.status),
      card('Indices', String(indices.length)),
      card('API', 'v1'))
  }

  async function refreshIndices() {
    state.indices = await api('/v1/indices')
    const list = byId('indexList')
    list.replaceChildren(...state.indices.map(index => {
      const article = document.createElement('article')
      const summary = document.createElement('span')
      summary.textContent = `${index.indexName} · ${index.documentCount} documents`
      const open = document.createElement('button')
      open.type = 'button'
      open.textContent = 'Open'
      open.addEventListener('click', () => selectIndex(index.indexName))
      article.append(summary, open)
      return article
    }))
  }

  async function createIndex(event) {
    event.preventDefault()
    const name = byId('createIndexName').value.trim()
    const schema = parseJson(byId('createIndexSchema').value)
    await api(`/v1/indices/${encodeURIComponent(name)}`, {
      method: 'PUT',
      body: JSON.stringify({ indexName: name, schema, topology: { shardCount: 1, replicaCount: 0 }, settings: { refreshInterval: null, commitInterval: null, defaultField: schema.fields[0]?.name || null, maximumQueryClauses: null } })
    })
    setNotice(`Created ${name}.`)
    byId('createIndexName').value = ''
    await refreshIndices()
    await selectIndex(name)
  }

  async function selectIndex(name) {
    state.index = name
    byId('selectedIndexName').textContent = name
    byId('indexNavigation').hidden = false
    show('index-overview')
  }

  async function refreshIndexView(view) {
    const name = encodeURIComponent(state.index)
    if (view === 'index-overview') byId('indexOverview').textContent = format(await api(`/v1/indices/${name}/stats`))
    if (view === 'schema' || view === 'settings') {
      const schema = await api(`/v1/indices/${name}/schema`)
      byId('schemaOutput').textContent = format(schema)
      byId('settingsJson').value = JSON.stringify(schema.settings, null, 2)
    }
    if (view === 'documents') byId('documentsOutput').textContent = format(await inspect('documents'))
    if (view === 'segments') byId('segmentsOutput').textContent = format(await inspect('segments'))
  }

  async function inspect(resource) {
    return api(`/v1/indices/${encodeURIComponent(state.index)}/inspection/${resource}?limit=100`)
  }

  async function indexDocument(event) {
    event.preventDefault()
    ensureIndex()
    const documentId = byId('documentId').value.trim()
    const documentValue = parseJson(byId('documentJson').value)
    const result = await api(`/v1/indices/${encodeURIComponent(state.index)}/documents:bulk`, {
      method: 'POST',
      body: JSON.stringify({ indexName: state.index, operations: [{ kind: 0, documentId, document: documentValue }], refresh: true })
    })
    byId('documentsOutput').textContent = format(result)
    setNotice(`Indexed ${documentId}.`)
  }

  async function runSearch() {
    ensureIndex()
    const result = await api(`/v1/indices/${encodeURIComponent(state.index)}/search`, {
      method: 'POST',
      body: JSON.stringify({ query: parseJson(byId('queryJson').value), size: 20, includeDocuments: true })
    })
    byId('queryOutput').textContent = format(result)
  }

  async function runExplain() {
    ensureIndex()
    const documentId = byId('explainDocumentId').value.trim()
    if (!documentId) throw new Error('Enter a document ID to explain.')
    const result = await api(`/v1/indices/${encodeURIComponent(state.index)}/explain`, {
      method: 'POST',
      body: JSON.stringify({ documentId, query: parseJson(byId('queryJson').value) })
    })
    byId('queryOutput').textContent = format(result)
  }

  async function saveSettings(event) {
    event.preventDefault()
    ensureIndex()
    const token = await confirmationToken('update-settings', state.index)
    await api(`/v1/indices/${encodeURIComponent(state.index)}/settings`, {
      method: 'PATCH',
      headers: { 'X-LeanCorpus-Confirm': token },
      body: JSON.stringify({ indexName: state.index, settings: parseJson(byId('settingsJson').value) })
    })
    setNotice(`Saved settings for ${state.index}.`)
  }

  async function deleteIndex() {
    ensureIndex()
    if (!window.confirm(`Delete index '${state.index}' and its local data?`)) return
    const name = state.index
    const token = await confirmationToken('delete-index', name)
    await api(`/v1/indices/${encodeURIComponent(name)}`, { method: 'DELETE', headers: { 'X-LeanCorpus-Confirm': token } })
    state.index = null
    byId('indexNavigation').hidden = true
    setNotice(`Deleted ${name}.`)
    await refreshIndices()
    show('indices')
  }

  async function confirmationToken(operation, resource) {
    const bytes = new TextEncoder().encode(`leancorpus-community-confirm\0${operation}\0${resource}`)
    const digest = await crypto.subtle.digest('SHA-256', bytes)
    return [...new Uint8Array(digest)].map(value => value.toString(16).padStart(2, '0')).join('')
  }

  function card(title, value) {
    const element = document.createElement('article')
    element.className = 'card'
    const heading = document.createElement('strong')
    heading.textContent = title
    const content = document.createElement('p')
    content.textContent = value
    element.append(heading, content)
    return element
  }

  function parseJson(value) {
    try { return JSON.parse(value) } catch { throw new Error('The JSON value is invalid.') }
  }

  function format(value) { return JSON.stringify(value, null, 2) }
  function ensureIndex() { if (!state.index) throw new Error('Open an index first.') }
  function setNotice(message) { notice.textContent = message }
  function report(error) { notice.textContent = error instanceof Error ? error.message : String(error) }

  window.addEventListener('unhandledrejection', event => { event.preventDefault(); report(event.reason) })
})()
