using System;
using System.Threading;
using System.Threading.Tasks;
using ArxisStudio.Markup.Xaml;
using Avalonia;
using Avalonia.Headless;

namespace ArxisStudio.Markup.Xaml.Loader.Sample;

/// <summary>
/// A showcase of what the three packages do, driven entirely through their public API.
/// </summary>
/// <remarks>
/// <para>
/// This demonstrates library usage and nothing else. It has no window, no selection, no property
/// inspector and no way to edit anything by pointing at it — all of which the contract's
/// out-of-scope list rules out, and any of which would make this the visual designer the
/// repository exists not to be.
/// </para>
/// <para>
/// It runs on Avalonia's headless platform because building Avalonia objects needs Avalonia set
/// up, and a showcase of the loader that never builds one would be showing the wrong half. A
/// host with a real window does exactly what is done here and then puts the root object on
/// screen; that last step is the host's, not this library's.
/// </para>
/// </remarks>
internal static class Program
{
    private static async Task<int> Main()
    {
        // SetupWithoutStarting establishes Dispatcher.UIThread without running a message loop,
        // which is all the loader's default dispatcher needs.
        AppBuilder.Configure<Application>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .SetupWithoutStarting();

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        try
        {
            // SetupWithoutStarting makes this thread the one Avalonia objects belong to, so the
            // showcase runs inline rather than posting to a loop that is not turning.
            await RunAsync(cancellation.Token);

            Console.WriteLine();
            Console.WriteLine("Everything above ran against the packages as published. Nothing was compiled,");
            Console.WriteLine("and no document was rewritten. See docs/limitations.md for what is not here.");

            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"The showcase stopped: {error}");

            return 1;
        }
    }

    private static async Task RunAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("ArxisStudio.Markup — what the three packages do");
        Console.WriteLine();
        Console.WriteLine("  ArxisStudio.Markup              text, documents, workspace, dependencies");
        Console.WriteLine("  ArxisStudio.Markup.Xaml         lossless syntax model, editing, writing");
        Console.WriteLine("  ArxisStudio.Markup.Xaml.Loader  live Avalonia objects, resolution, updates");

        XamlDocument document = SyntaxShowcase.RoundTrip();

        SyntaxShowcase.Malformed();
        SyntaxShowcase.Edit(document);
        SyntaxShowcase.Values(document);

        WorkspaceShowcase.Workspace();

        await WorkspaceShowcase.GraphAsync(cancellationToken);

        (XamlLoadEnvironment environment, InMemoryResourceResolver resources) = LoaderShowcase.Environment();

        await using (XamlLoadSession session = await LoaderShowcase
            .LoadAsync(environment, XamlLoadMode.Runtime, cancellationToken))
        {
            LoaderShowcase.Mapping(session);
            LoaderShowcase.Edit(session);
        }

        await UpdateShowcase.DesignAsync(environment, cancellationToken);
        await UpdateShowcase.UpdatesAsync(environment, cancellationToken);
        await UpdateShowcase.SourceUpdateAsync(environment, resources, cancellationToken);
    }
}
