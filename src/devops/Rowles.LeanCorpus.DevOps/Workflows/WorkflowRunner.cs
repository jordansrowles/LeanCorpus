using Bullseye;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Workflows;

internal sealed class WorkflowRunner
{
    private const int MaximumRetainedEvents = 256;

    public async Task<WorkflowResult> RunAsync(
        IEnumerable<WorkflowTarget> targetDefinitions,
        IEnumerable<string> requestedTargets,
        Action<WorkflowProgressEvent>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(targetDefinitions);
        ArgumentNullException.ThrowIfNull(requestedTargets);
        var targets = targetDefinitions.ToArray();
        var requested = requestedTargets.ToArray();
        ValidateGraph(targets, requested);

        var definitions = targets.ToDictionary(target => target.Name, StringComparer.OrdinalIgnoreCase);
        var planned = GetDependencyClosure(definitions, requested);
        var events = new Queue<WorkflowProgressEvent>();
        var states = new Dictionary<string, WorkflowTargetState>(StringComparer.OrdinalIgnoreCase);
        var eventGate = new object();
        Exception? firstFailure = null;

        void Publish(string name, WorkflowTargetState state, Exception? exception = null)
        {
            var item = new WorkflowProgressEvent(name, state, DateTimeOffset.UtcNow, exception?.Message);
            lock (eventGate)
            {
                states[name] = state;
                events.Enqueue(item);
                while (events.Count > MaximumRetainedEvents)
                {
                    events.Dequeue();
                }
            }

            progress?.Invoke(item);
        }

        var workflow = new Targets();
        foreach (var target in targets)
        {
            workflow.Add(target.Name, target.Description, target.DependsOn, async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Publish(target.Name, WorkflowTargetState.Started);
                try
                {
                    await target.Action(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    Publish(target.Name, WorkflowTargetState.Succeeded);
                }
                catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
                {
                    Publish(target.Name, WorkflowTargetState.Cancelled, exception);
                    Interlocked.CompareExchange(ref firstFailure, exception, null);
                    throw;
                }
                catch (Exception exception)
                {
                    Publish(target.Name, WorkflowTargetState.Failed, exception);
                    Interlocked.CompareExchange(ref firstFailure, exception, null);
                    throw;
                }
            });
        }

        Exception? runnerFailure = null;
        try
        {
            await workflow.RunWithoutExitingAsync(
                requested,
                messageOnly: _ => true,
                outputWriter: TextWriter.Null,
                diagnosticsWriter: TextWriter.Null).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            runnerFailure = exception;
            Interlocked.CompareExchange(ref firstFailure, exception, null);
        }

        lock (eventGate)
        {
            foreach (var name in planned)
            {
                if (!states.ContainsKey(name))
                {
                    states[name] = WorkflowTargetState.Skipped;
                    var skipped = new WorkflowProgressEvent(name, WorkflowTargetState.Skipped, DateTimeOffset.UtcNow);
                    events.Enqueue(skipped);
                    while (events.Count > MaximumRetainedEvents)
                    {
                        events.Dequeue();
                    }
                }
            }

            var failed = states.Where(pair => pair.Value == WorkflowTargetState.Failed).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            var skippedTargets = states.Where(pair => pair.Value == WorkflowTargetState.Skipped).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            var outcome = cancellationToken.IsCancellationRequested || states.Values.Contains(WorkflowTargetState.Cancelled)
                ? OutcomeKind.Cancelled
                : failed.Length > 0
                    ? OutcomeKind.CommandFailure
                    : runnerFailure is not null
                        ? OutcomeKind.ValidationFailure
                        : OutcomeKind.Success;
            return new WorkflowResult(outcome, events.ToArray(), failed, skippedTargets, firstFailure?.Message);
        }
    }

    private static void ValidateGraph(IReadOnlyList<WorkflowTarget> targets, IReadOnlyList<string> requested)
    {
        if (targets.Count == 0 || requested.Count == 0)
        {
            throw new ArgumentException("At least one workflow target and one requested target are required.");
        }

        var definitions = new Dictionary<string, WorkflowTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(target.Name);
            ArgumentNullException.ThrowIfNull(target.Action);
            ArgumentNullException.ThrowIfNull(target.DependsOn);
            if (!definitions.TryAdd(target.Name, target))
            {
                throw new ArgumentException($"Workflow target '{target.Name}' is defined more than once.");
            }
        }

        foreach (var target in targets)
        {
            foreach (var dependency in target.DependsOn)
            {
                if (!definitions.ContainsKey(dependency))
                {
                    throw new ArgumentException($"Workflow target '{target.Name}' depends on unknown target '{dependency}'.");
                }
            }
        }

        foreach (var target in requested)
        {
            if (!definitions.ContainsKey(target))
            {
                throw new ArgumentException($"Requested workflow target '{target}' is not defined.");
            }
        }

        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string name)
        {
            if (visited.Contains(name))
            {
                return;
            }

            if (!visiting.Add(name))
            {
                throw new ArgumentException($"Workflow dependency cycle includes target '{name}'.");
            }

            foreach (var dependency in definitions[name].DependsOn)
            {
                Visit(dependency);
            }

            visiting.Remove(name);
            visited.Add(name);
        }

        foreach (var requestedTarget in requested)
        {
            Visit(requestedTarget);
        }
    }

    private static HashSet<string> GetDependencyClosure(IReadOnlyDictionary<string, WorkflowTarget> definitions, IEnumerable<string> requested)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string name)
        {
            if (!result.Add(name))
            {
                return;
            }

            foreach (var dependency in definitions[name].DependsOn)
            {
                Add(dependency);
            }
        }

        foreach (var name in requested)
        {
            Add(name);
        }

        return result;
    }
}
