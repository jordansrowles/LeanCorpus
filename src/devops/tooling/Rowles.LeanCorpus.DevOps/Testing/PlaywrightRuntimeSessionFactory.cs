using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.Loader;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed class PlaywrightRuntimeSessionFactory : IPlaywrightRuntimeSessionFactory
{
    public async Task<IPlaywrightRuntimeSession> CreateAsync(string assemblyPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        cancellationToken.ThrowIfCancellationRequested();

        var resolvedAssemblyPath = Path.GetFullPath(assemblyPath);
        var loadContext = new PlaywrightAssemblyLoadContext(Path.GetDirectoryName(resolvedAssemblyPath)!);
        object? playwright = null;
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(resolvedAssemblyPath);
            var playwrightType = RequireType(assembly, "Microsoft.Playwright.Playwright");
            var createMethod = RequireMethod(playwrightType, "CreateAsync", Type.EmptyTypes, BindingFlags.Public | BindingFlags.Static);
            playwright = await InvokeTaskAsync(createMethod, null, []).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Playwright.CreateAsync returned no runtime instance.");

            var playwrightContract = RequireType(assembly, "Microsoft.Playwright.IPlaywright");
            var chromium = playwrightContract.GetProperty("Chromium")?.GetValue(playwright)
                ?? throw new InvalidOperationException("The loaded Playwright runtime does not expose Chromium.");
            var browserTypeContract = RequireType(assembly, "Microsoft.Playwright.IBrowserType");
            var executablePath = browserTypeContract.GetProperty("ExecutablePath")?.GetValue(chromium) as string
                ?? throw new InvalidOperationException("The loaded Playwright runtime did not resolve a Chromium executable path.");

            return new PlaywrightRuntimeSession(loadContext, assembly, playwright, chromium, executablePath);
        }
        catch
        {
            (playwright as IDisposable)?.Dispose();
            loadContext.Unload();
            throw;
        }
    }

    private static Type RequireType(Assembly assembly, string name)
        => assembly.GetType(name, throwOnError: false, ignoreCase: false)
           ?? throw new InvalidOperationException($"The loaded Playwright assembly is missing type '{name}'.");

    private static MethodInfo RequireMethod(Type type, string name, Type[] parameterTypes, BindingFlags bindingFlags)
        => type.GetMethod(name, bindingFlags, binder: null, parameterTypes, modifiers: null)
           ?? throw new InvalidOperationException($"The loaded Playwright assembly is missing method '{type.FullName}.{name}'.");

    private static async Task<object?> InvokeTaskAsync(MethodInfo method, object? instance, object?[] arguments)
    {
        object? invocation;
        try
        {
            invocation = method.Invoke(instance, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }

        if (invocation is not Task task)
        {
            throw new InvalidOperationException($"Playwright method '{method.Name}' did not return a Task.");
        }

        await task.ConfigureAwait(false);
        return task.GetType().GetProperty("Result")?.GetValue(task);
    }

    private sealed class PlaywrightAssemblyLoadContext(string outputDirectory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var assemblyFileName = assemblyName.Name + ".dll";
            var candidatePath = Path.Combine(outputDirectory, assemblyFileName);
            return File.Exists(candidatePath) ? LoadFromAssemblyPath(candidatePath) : null;
        }
    }

    private sealed class PlaywrightRuntimeSession(
        PlaywrightAssemblyLoadContext loadContext,
        Assembly assembly,
        object playwright,
        object chromium,
        string chromiumExecutablePath) : IPlaywrightRuntimeSession
    {
        private int _disposed;

        public string ChromiumExecutablePath { get; } = chromiumExecutablePath;

        public async Task<string> LaunchHeadlessAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var optionsType = RequireType(assembly, "Microsoft.Playwright.BrowserTypeLaunchOptions");
            var options = Activator.CreateInstance(optionsType)
                ?? throw new InvalidOperationException("Could not create Playwright Chromium launch options.");
            var headlessProperty = optionsType.GetProperty("Headless")
                ?? throw new InvalidOperationException("Playwright Chromium launch options do not expose Headless.");
            headlessProperty.SetValue(options, true);

            var browserType = RequireType(assembly, "Microsoft.Playwright.IBrowserType");
            var launchMethod = RequireMethod(browserType, "LaunchAsync", [optionsType], BindingFlags.Public | BindingFlags.Instance);
            var browser = await InvokeTaskAsync(launchMethod, chromium, [options]).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Playwright Chromium launch returned no browser instance.");
            try
            {
                var browserContract = RequireType(assembly, "Microsoft.Playwright.IBrowser");
                return browserContract.GetProperty("Version")?.GetValue(browser) as string
                    ?? throw new InvalidOperationException("The launched Chromium browser did not report a version.");
            }
            finally
            {
                await CloseBrowserAsync(assembly, browser).ConfigureAwait(false);
            }
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            (playwright as IDisposable)?.Dispose();
            loadContext.Unload();
            return ValueTask.CompletedTask;
        }

        private static async Task CloseBrowserAsync(Assembly assembly, object browser)
        {
            var browserContract = RequireType(assembly, "Microsoft.Playwright.IBrowser");
            var closeOptionsType = RequireType(assembly, "Microsoft.Playwright.BrowserCloseOptions");
            var closeMethod = RequireMethod(browserContract, "CloseAsync", [closeOptionsType], BindingFlags.Public | BindingFlags.Instance);
            try
            {
                await InvokeTaskAsync(closeMethod, browser, [null]).ConfigureAwait(false);
            }
            finally
            {
                if (browser is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (browser is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}
