import { appendFileSync, readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const buildWorkflowPath = '.github/workflows/build.yml';
const docsArtifactName = 'leancorpus-docs-site';
const allowedBuildEvents = new Set(['push', 'workflow_dispatch']);
const productionSourceBranch = 'main';

export function resolveBuildRun(run, options) {
  validateBuildRunIdentity(run, options.repository);
  validateBuildRunTrigger(run, options);

  return {
    runId: run.id,
    headSha: run.head_sha,
    branch: run.head_branch,
    runNumber: run.run_number
  };
}

function validateBuildRunIdentity(run, repository) {
  if (!run || run.name !== 'Build' || run.path !== buildWorkflowPath) {
    throw new Error('The selected run is not the repository Build workflow.');
  }
  if (run.status !== 'completed' || run.conclusion !== 'success') {
    throw new Error(`Run ${run.id ?? '(unknown)'} is not a completed successful Build workflow run.`);
  }
  if (run.head_repository?.full_name !== repository) {
    throw new Error(`Run ${run.id} was produced by a different repository.`);
  }
  if (!allowedBuildEvents.has(run.event)) {
    throw new Error(`Build run ${run.id} uses an untrusted event: ${run.event ?? '(missing)'}.`);
  }
  if (!Number.isSafeInteger(run.id) || run.id <= 0 ||
      !Number.isSafeInteger(run.run_number) || run.run_number <= 0) {
    throw new Error('The selected Build run has an invalid identity.');
  }
  if (typeof run.head_sha !== 'string' || !/^[0-9a-f]{40}$/.test(run.head_sha)) {
    throw new Error(`Build run ${run.id} has a malformed source commit.`);
  }

  validateBuildRunBranch(run);
}

function validateBuildRunBranch(run) {
  if (typeof run.head_branch !== 'string' ||
      !/^[A-Za-z0-9._/-]+$/.test(run.head_branch) ||
      run.head_branch.split('/').some(part => part === '.' || part === '..')) {
    throw new Error(`Build run ${run.id} has an unsafe source branch.`);
  }
  if (run.head_branch !== productionSourceBranch) {
    throw new Error(`Build run ${run.id} from branch '${run.head_branch}' is not an authorised production source.`);
  }
}

function validateBuildRunTrigger(run, { eventName, eventPayload, requestedRunId }) {
  if (eventName === 'workflow_run') {
    validateAutomaticBuildRun(run, eventPayload);
    return;
  }
  if (eventName === 'workflow_dispatch') {
    validateRequestedBuildRun(run, requestedRunId);
    return;
  }

  throw new Error(`Unsupported docs deployment event: ${eventName ?? '(missing)'}.`);
}

function validateAutomaticBuildRun(run, eventPayload) {
  const sourceEvent = eventPayload?.workflow_run;
  if (run.event !== 'push') {
    throw new Error('Automatic docs deployment requires a trusted Build push event.');
  }
  if (sourceEvent?.id !== run.id || sourceEvent.conclusion !== 'success') {
    throw new Error('The completed Build event does not match the selected successful run.');
  }
  const message = sourceEvent.head_commit?.message;
  if (typeof message !== 'string' || !message.startsWith('[deploy]')) {
    throw new Error(`Build run ${run.id} does not opt in to automatic docs deployment.`);
  }
}

function validateRequestedBuildRun(run, requestedRunId) {
  const requestedId = parsePositiveRunId(requestedRunId);
  if (!Number.isSafeInteger(requestedId) || requestedId !== run.id) {
    throw new Error(`Selected Build run ${run.id} does not match the requested run ID.`);
  }
}

function parsePositiveRunId(value) {
  if (typeof value === 'number') return Number.isSafeInteger(value) && value > 0 ? value : Number.NaN;
  if (typeof value !== 'string' || !/^[1-9][0-9]*$/.test(value)) return Number.NaN;
  const parsed = Number(value);
  return Number.isSafeInteger(parsed) ? parsed : Number.NaN;
}

export function resolveDocsArtifact(artifacts, run) {
  const matches = artifacts.filter(artifact => artifact.name === docsArtifactName);
  if (matches.length !== 1) {
    throw new Error(`Build run ${run.id} must contain exactly one ${docsArtifactName} artefact.`);
  }

  const [artifact] = matches;
  if (!Number.isSafeInteger(artifact.id) || artifact.id <= 0 || artifact.expired !== false ||
      !Number.isSafeInteger(artifact.size_in_bytes) || artifact.size_in_bytes <= 0) {
    throw new Error(`Build run ${run.id} has no downloadable ${docsArtifactName} artefact.`);
  }
  if (typeof artifact.digest !== 'string' || !/^sha256:[0-9a-f]{64}$/.test(artifact.digest)) {
    throw new Error(`Build run ${run.id} has no valid ${docsArtifactName} SHA-256 digest.`);
  }
  if (artifact.workflow_run?.id !== run.id ||
      artifact.workflow_run?.head_sha !== run.head_sha ||
      artifact.workflow_run?.head_branch !== run.head_branch) {
    throw new Error(`The ${docsArtifactName} artefact identity does not match Build run ${run.id}.`);
  }

  return { artifactId: artifact.id, artifactDigest: artifact.digest };
}

async function main() {
  const eventName = process.env.GITHUB_EVENT_NAME;
  const repository = process.env.GITHUB_REPOSITORY;
  const eventPath = process.env.GITHUB_EVENT_PATH;
  const outputPath = process.env.GITHUB_OUTPUT;
  const token = process.env.GITHUB_TOKEN;
  const apiUrl = new URL(process.env.GH_API_URL ?? 'https://api.github.com');
  if (apiUrl.protocol !== 'https:') {
    throw new Error('GitHub API must use HTTPS.');
  }
  if (!repository || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository) ||
      !eventPath || !outputPath || !token) {
    throw new Error('GitHub repository, event, output and token context is required.');
  }

  const eventPayload = JSON.parse(readFileSync(eventPath, 'utf8'));
  const requestedRunId = eventName === 'workflow_run'
    ? eventPayload.workflow_run?.id
    : eventPayload.inputs?.run_id;
  const requestedId = parsePositiveRunId(requestedRunId);
  if (!Number.isSafeInteger(requestedId) || requestedId <= 0) {
    throw new Error('The docs deployment event has no valid Build run ID.');
  }

  const apiBase = apiUrl.toString().replace(/\/$/, '');
  const response = await fetch(`${apiBase}/repos/${repository}/actions/runs/${requestedId}`, {
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28'
    },
    signal: AbortSignal.timeout(20_000)
  });
  if (!response.ok) {
    throw new Error(`GitHub rejected Build run lookup (${response.status} ${response.statusText}).`);
  }

  const run = await response.json();
  const admitted = resolveBuildRun(run, { repository, eventName, eventPayload, requestedRunId });
  const artifactsResponse = await fetch(`${apiBase}/repos/${repository}/actions/runs/${admitted.runId}/artifacts?name=${docsArtifactName}&per_page=100`, {
    headers: {
      Accept: 'application/vnd.github+json',
      Authorization: `Bearer ${token}`,
      'X-GitHub-Api-Version': '2022-11-28'
    },
    signal: AbortSignal.timeout(20_000)
  });
  if (!artifactsResponse.ok) {
    throw new Error(`GitHub rejected docs artefact lookup (${artifactsResponse.status} ${artifactsResponse.statusText}).`);
  }
  const artifactDocument = await artifactsResponse.json();
  const artifact = resolveDocsArtifact(artifactDocument.artifacts ?? [], run);
  appendFileSync(outputPath,
    `run-id=${admitted.runId}\nhead-sha=${admitted.headSha}\nbranch=${admitted.branch}\nrun-number=${admitted.runNumber}\nartifact-id=${artifact.artifactId}\nartifact-digest=${artifact.artifactDigest}\n`);
  console.log(`Admitted docs artefact ${artifact.artifactId} (${artifact.artifactDigest}) from Build run ${admitted.runNumber} at ${admitted.headSha}.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await main();
}
