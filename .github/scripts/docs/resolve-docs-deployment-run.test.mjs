import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { resolveBuildRun, resolveDocsArtifact } from './resolve-docs-deployment-run.mjs';

const repository = 'jordansrowles/LeanCorpus';

function buildRun(overrides = {}) {
  return {
    id: 39843175610,
    name: 'Build',
    path: '.github/workflows/build.yml',
    status: 'completed',
    conclusion: 'success',
    event: 'push',
    head_branch: 'main',
    head_sha: 'a'.repeat(40),
    head_repository: { full_name: repository },
    head_commit: { message: '[deploy] Publish docs' },
    run_number: 361,
    ...overrides
  };
}

function workflowRunPayload(run, overrides = {}) {
  return {
    workflow_run: {
      id: run.id,
      conclusion: run.conclusion,
      head_commit: run.head_commit,
      ...overrides
    }
  };
}

function docsArtifact(run, overrides = {}) {
  return {
    id: 12449571013,
    name: 'leancorpus-docs-site',
    expired: false,
    size_in_bytes: 1024,
    digest: `sha256:${'b'.repeat(64)}`,
    workflow_run: {
      id: run.id,
      head_sha: run.head_sha,
      head_branch: run.head_branch
    },
    ...overrides
  };
}

test('admits a successful trusted Build push for automatic deployment', () => {
  const run = buildRun();
  const result = resolveBuildRun(run, {
    repository,
    eventName: 'workflow_run',
    eventPayload: workflowRunPayload(run)
  });

  assert.deepEqual(result, {
    runId: run.id,
    headSha: run.head_sha,
    branch: run.head_branch,
    runNumber: run.run_number
  });
});

test('restricts the automatic deployment trigger and job to main', () => {
  const workflow = readFileSync('.github/workflows/deploy.yml', 'utf8');

  assert.match(workflow, /workflow_run:\s*\n\s+workflows:\s*\["Build"\]\s*\n\s+types:\s*\[completed\]\s*\n\s+branches:\s*\[main\]/);
  assert.match(workflow, /github\.event\.workflow_run\.head_branch == 'main'/);
  assert.match(workflow, /node \.github\/scripts\/docs\/resolve-docs-deployment-run\.mjs/);
});

test('rejects automatic production deployment from an unapproved source branch', () => {
  const run = buildRun({ head_branch: 'issue-97' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_run',
    eventPayload: workflowRunPayload(run)
  }), /not an authorised production source/);
});

test('rejects a same-repository pull request run even when manually selected', () => {
  const run = buildRun({ event: 'pull_request', head_branch: 'feature/docs' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  }), /untrusted event/);
});

test('rejects a pull request workflow_run before considering its commit message', () => {
  const run = buildRun({ event: 'pull_request' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_run',
    eventPayload: workflowRunPayload(run)
  }), /untrusted event/);
});

test('rejects a Build run produced by a fork', () => {
  const run = buildRun({ head_repository: { full_name: 'contributor/LeanCorpus' } });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  }), /different repository/);
});

test('rejects a workflow with a misleading Build display name', () => {
  const run = buildRun({ path: '.github/workflows/untrusted-build.yml' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  }), /not the repository Build workflow/);
});

test('rejects unsafe Build branch names', () => {
  const run = buildRun({ head_branch: 'feature/../main' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  }), /unsafe source branch/);
});

test('rejects a failed, incomplete or malformed source run', () => {
  for (const run of [
    buildRun({ conclusion: 'failure' }),
    buildRun({ status: 'in_progress' }),
    buildRun({ head_sha: 'not-a-commit' })
  ]) {
    assert.throws(() => resolveBuildRun(run, {
      repository,
      eventName: 'workflow_dispatch',
      requestedRunId: String(run.id)
    }));
  }
});

test('rejects automatic deployment unless the source commit opts in', () => {
  const run = buildRun({ head_commit: { message: 'Ordinary change' } });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_run',
    eventPayload: workflowRunPayload(run)
  }), /does not opt in/);
});

test('allows manual recovery by selecting an earlier successful Build run', () => {
  const previousRun = buildRun({ id: 39800000000, head_branch: 'main', run_number: 347 });
  const result = resolveBuildRun(previousRun, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(previousRun.id)
  });

  assert.equal(result.runId, previousRun.id);
  assert.equal(result.branch, 'main');
  assert.equal(result.headSha, previousRun.head_sha);
});

test('rejects manual production deployment from an unapproved source branch', () => {
  const run = buildRun({ head_branch: 'issue-97' });

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  }), /not an authorised production source/);
});

test('rejects manual selection of a different run ID', () => {
  const run = buildRun();

  assert.throws(() => resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id + 1)
  }), /does not match/);
});

test('requires manual run IDs to use a canonical positive decimal value', () => {
  const run = buildRun();

  for (const requestedRunId of ['0', '-1', `0${run.id}`, `${run.id}e0`, `${run.id}.0`]) {
    assert.throws(() => resolveBuildRun(run, {
      repository,
      eventName: 'workflow_dispatch',
      requestedRunId
    }), /does not match/);
  }
});

test('selects the exact unexpired docs artifact from the admitted Build run', () => {
  const run = buildRun();
  const artifact = docsArtifact(run);

  assert.deepEqual(resolveDocsArtifact([artifact], run), {
    artifactId: artifact.id,
    artifactDigest: artifact.digest
  });
});

test('rejects missing, expired, duplicated or mismatched docs artifacts', () => {
  const run = buildRun();
  const artifact = docsArtifact(run);

  for (const artifacts of [
    [],
    [docsArtifact(run, { expired: true })],
    [artifact, { ...artifact, id: artifact.id + 1 }],
    [docsArtifact(run, { workflow_run: { ...artifact.workflow_run, head_sha: 'c'.repeat(40) } })],
    [docsArtifact(run, { digest: 'sha256:invalid' })]
  ]) {
    assert.throws(() => resolveDocsArtifact(artifacts, run));
  }
});

test('manual recovery resolves the earlier validated artifact instead of rebuilding', () => {
  const earlierRun = buildRun({ id: 39800000000, head_branch: 'main', run_number: 347 });
  const earlierArtifact = docsArtifact(earlierRun, { id: 12440000000 });
  const selectedRun = resolveBuildRun(earlierRun, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(earlierRun.id)
  });

  assert.equal(selectedRun.runId, earlierRun.id);
  assert.equal(resolveDocsArtifact([earlierArtifact], earlierRun).artifactId, earlierArtifact.id);
});

test('automatic and manual admission select the same validated artifact identity', () => {
  const run = buildRun();
  const artifact = docsArtifact(run);
  const automaticRun = resolveBuildRun(run, {
    repository,
    eventName: 'workflow_run',
    eventPayload: workflowRunPayload(run)
  });
  const manualRun = resolveBuildRun(run, {
    repository,
    eventName: 'workflow_dispatch',
    requestedRunId: String(run.id)
  });
  const automaticArtifact = resolveDocsArtifact([artifact], {
    ...run,
    id: automaticRun.runId,
    head_sha: automaticRun.headSha,
    head_branch: automaticRun.branch
  });
  const manualArtifact = resolveDocsArtifact([artifact], {
    ...run,
    id: manualRun.runId,
    head_sha: manualRun.headSha,
    head_branch: manualRun.branch
  });

  assert.deepEqual(manualRun, automaticRun);
  assert.deepEqual(manualArtifact, automaticArtifact);
});
