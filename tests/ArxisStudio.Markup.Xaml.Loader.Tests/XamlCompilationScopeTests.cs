using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Markup.Xaml.Loader.Tests;

/// <summary>
/// The environment's compilation scope brackets everything that runs the compiler.
/// </summary>
/// <remarks>
/// The scope exists for a host whose assemblies live in a replaceable load context — the process's
/// runtime compiler keeps state the session cannot see, and the environment's owner is the one who
/// knows where it has to be brought to. These tests pin the contract the session keeps: entered for
/// the load, entered again for every update, balanced whether or not the operation succeeded, and
/// never required.
/// </remarks>
public sealed class XamlCompilationScopeTests
{
    private const string Avalonia = "https://github.com/avaloniaui";

    private static readonly Uri ViewUri = new("file:///Views/View.axaml");

    private static XamlDocument Document(string content = "<TextBlock Text=\"hello\" />") =>
        XamlDocument.Parse(
            $"<StackPanel xmlns=\"{Avalonia}\">{content}</StackPanel>",
            new XamlParseOptions { DocumentUri = ViewUri });

    private static XamlLoadEnvironment Environment(CountingScope scope) =>
        new()
        {
            SourceProvider = XamlLoadEnvironment.CreateDefault().SourceProvider,
            AssemblyResolver = XamlLoadEnvironment.CreateDefault().AssemblyResolver,
            TypeResolver = XamlLoadEnvironment.CreateDefault().TypeResolver,
            ResourceResolver = XamlLoadEnvironment.CreateDefault().ResourceResolver,
            CompilationScope = scope,
        };

    [AvaloniaFact]
    public async Task TryCreateAsync_EntersTheScopeAndLeavesIt()
    {
        var scope = new CountingScope();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Document(),
            Environment(scope),
            options: null,
            TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.NotNull(session);
            Assert.True(scope.Entered > 0);
            Assert.Equal(scope.Entered, scope.Exited);
        }

        _ = result;
    }

    [AvaloniaFact]
    public async Task ApplyDocumentUpdateAsync_EntersTheScopeForTheRebuild()
    {
        var scope = new CountingScope();

        (XamlLoadSession? session, _) = await XamlLoadSession.TryCreateAsync(
            Document(),
            Environment(scope),
            options: null,
            TestContext.Current.CancellationToken);

        Assert.NotNull(session);

        await using (session)
        {
            int beforeUpdate = scope.Entered;

            XamlDocument updated = session.Document
                .Edit()
                .InsertElement(session.Document.Root!, 1, "<Border />")
                .Apply();

            XamlUpdateResult applied = await session.ApplyDocumentUpdateAsync(
                updated, TestContext.Current.CancellationToken);

            Assert.True(applied.Applied);
            Assert.True(scope.Entered > beforeUpdate);
            Assert.Equal(scope.Entered, scope.Exited);
        }
    }

    /// <summary>A document that cannot load still leaves the scope balanced.</summary>
    /// <remarks>
    /// The scope moves process-wide state around; an exit that depended on the load succeeding
    /// would leave the process compiling in the wrong place from the first bad document onward.
    /// </remarks>
    [AvaloniaFact]
    public async Task TryCreateAsync_LeavesTheScope_WhenTheDocumentDoesNotLoad()
    {
        var scope = new CountingScope();

        (XamlLoadSession? session, XamlLoadResult result) = await XamlLoadSession.TryCreateAsync(
            Document("<NoSuchControlAnywhere />"),
            Environment(scope),
            options: null,
            TestContext.Current.CancellationToken);

        await using (session)
        {
            Assert.True(scope.Entered > 0);
            Assert.Equal(scope.Entered, scope.Exited);
        }

        _ = result;
    }

    private sealed class CountingScope : IXamlCompilationScope
    {
        private int _entered;
        private int _exited;

        public int Entered => Volatile.Read(ref _entered);

        public int Exited => Volatile.Read(ref _exited);

        public IDisposable Enter()
        {
            Interlocked.Increment(ref _entered);

            return new Exit(this);
        }

        private sealed class Exit(CountingScope owner) : IDisposable
        {
            public void Dispose() => Interlocked.Increment(ref owner._exited);
        }
    }
}
