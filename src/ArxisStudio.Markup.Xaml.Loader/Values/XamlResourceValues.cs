using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>An attribute whose value is read from the resource its text names (ADR 0031).</summary>
/// <param name="Element">The element the attribute is written on.</param>
/// <param name="Attribute">The attribute.</param>
/// <param name="Member">The member it writes, as the element's type has it.</param>
internal sealed record XamlResourceAttribute(XamlElement Element, XamlAttribute Attribute, XamlMemberDescriptor Member);

/// <summary>A value read from a resource, or why there is none.</summary>
/// <param name="Value">The value, meaningful only when <paramref name="Failure"/> is <see langword="null"/>.</param>
/// <param name="Failure">What went wrong, said where the attribute is written.</param>
internal readonly record struct XamlResourceRead(object? Value, MarkupDiagnostic? Failure);

/// <summary>
/// Reads the values a document names by URI — an image's source, a window's icon — through the
/// environment, rather than leaving Avalonia's asset loader to look for them (ADR 0031).
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's loader reads <c>Source="/Assets/logo.png"</c> through its own asset loader, which knows
/// the resources of the assemblies the process has loaded and nothing else: an image added since the
/// last build is not there, a resource the environment's resolvers would have found is never asked
/// for, and an image it cannot find fails the whole document. Here the text is a URI against the
/// document's base, the environment's resolvers find what it names — what a designer has on disk
/// rather than what was last built — and a name that finds nothing is a diagnostic about one attribute.
/// </para>
/// <para>
/// The attribute is withheld from the text Avalonia is given, and the value is set on the object once
/// the object exists: after a load, after a part is rebuilt, and in place when the text changes. A
/// value that is read is kept for the environment by where it was found and what it held, so a part
/// rebuilt or a document loaded again does not decode the same image twice, and an image whose file
/// changed is decoded afresh.
/// </para>
/// </remarks>
internal static class XamlResourceValues
{
    /// <summary>Reports whether a member of a type reads its text as the URI of a resource.</summary>
    /// <remarks>
    /// The types Avalonia's own compiler reads an image into: an image wherever one is shown, a brush's
    /// source, a bitmap named as one, and a window's icon. A member typed <see cref="object"/> could
    /// hold a bitmap too, and reads text as text — a button's content is not a file name.
    /// </remarks>
    /// <param name="valueType">The type the member holds.</param>
    /// <returns><see langword="true"/> for an image or an icon.</returns>
    internal static bool Reads(Type valueType) =>
        valueType == typeof(IImage)
        || valueType == typeof(IImageBrushSource)
        || valueType == typeof(Bitmap)
        || valueType == typeof(WindowIcon);

    /// <summary>Reads the value of one attribute: the resource its text names, decoded as its member holds it.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <param name="member">The member it writes.</param>
    /// <param name="baseUri">What a relative URI is resolved against — the document's base.</param>
    /// <param name="environment">The environment whose resolvers find the resource.</param>
    /// <param name="documentUri">The document the attribute is written in, for the diagnostic.</param>
    /// <param name="severity">
    /// How a failure is said: a warning at a load, which carries on without the value, and an error at
    /// an update, which is refused.
    /// </param>
    /// <param name="cancellationToken">A token to observe while resolving and reading.</param>
    /// <returns>The value, or what went wrong.</returns>
    internal static async ValueTask<XamlResourceRead> ReadAsync(
        XamlAttribute attribute,
        XamlMemberDescriptor member,
        Uri? baseUri,
        XamlLoadEnvironment environment,
        Uri? documentUri,
        MarkupDiagnosticSeverity severity,
        CancellationToken cancellationToken)
    {
        string text = attribute.GetValueText().Trim();
        TextSpan span = attribute.ValueSpan ?? attribute.Span;

        // Written empty, an image is no image: the same as not writing the attribute.
        if (text.Length == 0)
        {
            return new XamlResourceRead(null, null);
        }

        if (!Uri.TryCreate(text, UriKind.RelativeOrAbsolute, out Uri? uri))
        {
            return Failed(
                XamlLoaderDiagnosticCodes.UnresolvedResource,
                $"'{text}' is not a URI, so {member.Name} has nothing to be read from.");
        }

        Uri found;
        byte[] bytes;

        try
        {
            XamlResource? resource = await environment.ResourceResolver
                .ResolveAsync(uri, baseUri, cancellationToken)
                .ConfigureAwait(false);

            if (resource is null)
            {
                // What the environment does not know may still be built into an assembly the process has
                // loaded — a package's image, say — and before ADR 0031 Avalonia's asset loader was the only
                // one asked about it. It is asked last, as an include the environment cannot find is left to
                // Avalonia.
                if (BuiltIn(uri, baseUri) is not { } asset)
                {
                    return Failed(
                        XamlLoaderDiagnosticCodes.UnresolvedResource,
                        $"'{text}' names nothing the environment can find, so {member.Name} was not set.");
                }

                (found, bytes) = asset;
            }
            else
            {
                await using Stream stream = await resource.OpenAsync(cancellationToken).ConfigureAwait(false);
                using var copy = new MemoryStream();

                await stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
                found = resource.Uri;
                bytes = copy.ToArray();
            }
        }
        catch (Exception unread) when (unread is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Failed(
                XamlLoaderDiagnosticCodes.UnreadableResourceValue,
                $"'{text}' was found but could not be read: {unread.Message}");
        }

        Type kind = KindOf(member.ValueType);

        if (environment.ResourceValues.TryGet(found, kind, bytes, out object? known))
        {
            return new XamlResourceRead(known, null);
        }

        // Decoded on the owning thread: a window's icon is a platform object, and what a bitmap is
        // decoded into is the renderer's, which is only promised to that thread.
        (object? value, string? error) = await environment.Dispatcher
            .InvokeAsync(() => Decode(kind, bytes), cancellationToken)
            .ConfigureAwait(false);

        if (value is null)
        {
            return Failed(
                XamlLoaderDiagnosticCodes.UnreadableResourceValue,
                $"'{text}' was found at {found} but is not {(kind == typeof(WindowIcon) ? "an icon" : "an image")}: {error}");
        }

        environment.ResourceValues.Remember(found, kind, bytes, value);

        return new XamlResourceRead(value, null);

        XamlResourceRead Failed(string code, string message) =>
            new(null, MarkupDiagnostic.Resolution(code, message, severity, documentUri, span));
    }

    /// <summary>
    /// Reads the values of the attributes a load withheld and sets each on the object its element
    /// produced.
    /// </summary>
    /// <remarks>
    /// Read off the owning thread and set on it, in one turn, once everything is read. A value that
    /// cannot be read is a warning and the object goes without it, as an image a running application
    /// cannot find shows nothing; a setter that refuses one is a warning too.
    /// </remarks>
    /// <param name="attributes">The attributes the load withheld.</param>
    /// <param name="objects">What each element produced.</param>
    /// <param name="baseUri">The document's base.</param>
    /// <param name="environment">The environment the load used.</param>
    /// <param name="documentUri">The document's URI.</param>
    /// <param name="diagnostics">Collects everything that went wrong.</param>
    /// <param name="cancellationToken">A token to observe while reading.</param>
    /// <returns>A task that completes once every value that could be read is set.</returns>
    internal static async ValueTask ApplyAsync(
        ImmutableArray<XamlResourceAttribute> attributes,
        XamlObjectMap objects,
        Uri? baseUri,
        XamlLoadEnvironment environment,
        Uri? documentUri,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        if (attributes.IsEmpty)
        {
            return;
        }

        List<(XamlResourceAttribute Attribute, object? Value)> read = await ReadAllAsync(
                attributes, baseUri, environment, documentUri, diagnostics, cancellationToken)
            .ConfigureAwait(false);

        await environment.Dispatcher
            .InvokeAsync<object?>(
                () =>
                {
                    Set(read, objects, [], documentUri, diagnostics);

                    return null;
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reads the values of several attributes, each failure said as a warning.</summary>
    internal static async ValueTask<List<(XamlResourceAttribute Attribute, object? Value)>> ReadAllAsync(
        IEnumerable<XamlResourceAttribute> attributes,
        Uri? baseUri,
        XamlLoadEnvironment environment,
        Uri? documentUri,
        List<MarkupDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var read = new List<(XamlResourceAttribute Attribute, object? Value)>();

        foreach (XamlResourceAttribute attribute in attributes)
        {
            XamlResourceRead value = await ReadAsync(
                    attribute.Attribute,
                    attribute.Member,
                    baseUri,
                    environment,
                    documentUri,
                    MarkupDiagnosticSeverity.Warning,
                    cancellationToken)
                .ConfigureAwait(false);

            if (value.Failure is { } failure)
            {
                diagnostics.Add(failure);

                continue;
            }

            read.Add((attribute, value.Value));
        }

        return read;
    }

    /// <summary>
    /// Sets values read for attributes on the objects their elements produced, except on an element
    /// whose object was kept — it has the value it was built with.
    /// </summary>
    /// <remarks>On the owning thread.</remarks>
    internal static void Set(
        List<(XamlResourceAttribute Attribute, object? Value)> read,
        XamlObjectMap objects,
        IReadOnlyCollection<XamlElement> kept,
        Uri? documentUri,
        List<MarkupDiagnostic> diagnostics)
    {
        foreach ((XamlResourceAttribute attribute, object? value) in read)
        {
            if (IsKept(kept, attribute.Element) || objects.GetObject(attribute.Element) is not { } target)
            {
                continue;
            }

            try
            {
                XamlDesignValues.Write(target, attribute.Member, value);
            }
            catch (Exception error) when (XamlDesignValues.Refused(error))
            {
                diagnostics.Add(MarkupDiagnostic.Load(
                    XamlLoaderDiagnosticCodes.IncompatibleValue,
                    $"{target.GetType().Name}.{attribute.Member.Name} would not take what '{attribute.Attribute.GetValueText()}' " +
                    $"names: {XamlDesignValues.Unwrapped(error).Message}",
                    MarkupDiagnosticSeverity.Warning,
                    documentUri,
                    attribute.Attribute.Span));
            }
        }
    }

    private static bool IsKept(IReadOnlyCollection<XamlElement> kept, XamlElement element)
    {
        foreach (XamlElement candidate in kept)
        {
            if (ReferenceEquals(candidate, element))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads what Avalonia's asset loader finds at a URI: a resource built into an assembly the process has
    /// loaded.
    /// </summary>
    /// <param name="uri">The URI as written.</param>
    /// <param name="baseUri">What a relative URI is resolved against.</param>
    /// <returns>Where it was found and what it holds; <see langword="null"/> — nothing there, or nothing to ask.</returns>
    /// <remarks>
    /// Only an <c>avares:</c> URI is the asset loader's. Without an application the loader has no platform to
    /// ask, and that is the same answer as nothing found.
    /// </remarks>
    private static (Uri Uri, byte[] Bytes)? BuiltIn(Uri uri, Uri? baseUri)
    {
        Uri target = InMemoryResourceResolver.Combine(uri, baseUri);

        if (!XamlUri.IsAvaloniaResource(target))
        {
            return null;
        }

        try
        {
            if (!AssetLoader.Exists(target))
            {
                return null;
            }

            using Stream stream = AssetLoader.Open(target);
            using var copy = new MemoryStream();

            stream.CopyTo(copy);

            return (target, copy.ToArray());
        }
        catch (Exception missing) when (missing is IOException or InvalidOperationException or ArgumentException
                                            or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>What a member's resource is decoded into: an icon for a window, a bitmap for the rest.</summary>
    private static Type KindOf(Type valueType) => valueType == typeof(WindowIcon) ? typeof(WindowIcon) : typeof(Bitmap);

    /// <summary>Decodes a resource's bytes, or says why they are not what the member holds.</summary>
    /// <remarks>
    /// Whatever the decoder throws is the answer "these bytes are not an image": the bytes are the
    /// author's file, and listing the exceptions one platform's decoder raises would let another's
    /// throw its way out of a load.
    /// </remarks>
    private static (object? Value, string? Error) Decode(Type kind, byte[] bytes)
    {
        try
        {
            return kind == typeof(WindowIcon)
                ? (new WindowIcon(new MemoryStream(bytes, writable: false)), null)
                : (new Bitmap(new MemoryStream(bytes, writable: false)), null);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return (null, error.Message);
        }
    }
}

/// <summary>
/// The values an environment has read from resources, by where each was found and what it held.
/// </summary>
/// <remarks>
/// One per environment, for the reason the member resolver is: what it keeps is made from the
/// environment's resources, and a rebuilt project is a new environment whose answers may differ. A
/// value is reused only for the same bytes, so a file changed on disk is decoded again however the
/// resolver found it — the bytes are read either way, and reading is what is cheap.
/// </remarks>
internal sealed class XamlResourceValueCache
{
    private readonly Lock _gate = new();
    private readonly Dictionary<(string Uri, Type Kind), (byte[] Bytes, object Value)> _values = [];

    /// <summary>Finds a value read before from the same bytes at the same place.</summary>
    internal bool TryGet(Uri uri, Type kind, byte[] bytes, out object? value)
    {
        lock (_gate)
        {
            if (_values.TryGetValue((uri.OriginalString, kind), out (byte[] Bytes, object Value) known)
                && known.Bytes.AsSpan().SequenceEqual(bytes))
            {
                value = known.Value;

                return true;
            }
        }

        value = null;

        return false;
    }

    /// <summary>Keeps a value read from a resource, in place of whatever was read there before.</summary>
    internal void Remember(Uri uri, Type kind, byte[] bytes, object value)
    {
        lock (_gate)
        {
            _values[(uri.OriginalString, kind)] = (bytes, value);
        }
    }
}
