using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// An element lifted out of the document it was written in, with the namespaces it is written in —
/// what crosses from one document to another through a clipboard, a drag or an extraction.
/// </summary>
/// <remarks>
/// <para>
/// An element's text alone does not say what it means. <c>&lt;local:Badge /&gt;</c> names whatever
/// its document bound <c>local</c> to, and that binding sits on an ancestor — almost always the root,
/// because Avalonia accepts <c>xmlns</c> nowhere else. A fragment carries the declarations its text
/// uses, written onto its own element, which makes it a document in its own right: it parses on
/// its own, goes on a clipboard and comes back, and
/// <see cref="XamlDocumentEditor.InsertFragment"/> can reconcile its prefixes with the document it
/// lands in.
/// </para>
/// <para>
/// It also carries which of those namespaces its source said a reader may ignore — the design
/// namespace, usually — because a <c>d:</c> attribute a compiler has not been told to skip is a
/// build error in the document that receives it.
/// </para>
/// <para>
/// Its text is left-aligned: the indentation the element had where it sat is taken off every line,
/// so that wherever it lands it is indented to fit. Text a control displays is not indentation and
/// is not touched.
/// </para>
/// </remarks>
public sealed class XamlFragment
{
    /// <summary>The local name of the markup-compatibility attribute that lists ignorable prefixes.</summary>
    private const string Ignorable = "Ignorable";

    private XamlFragment(XamlDocument document, Uri? sourceUri)
    {
        Document = document;
        SourceUri = sourceUri;
        Root = document.Root;
        Namespaces = Root is null ? ImmutableDictionary<string, string>.Empty : DeclarationsOf(Root);
        IgnorableNamespaces = Root is null ? [] : IgnorableNamespacesOf(Root);
    }

    /// <summary>
    /// Gets the fragment as a document of its own: its element at the root, carrying every
    /// declaration the element's text uses.
    /// </summary>
    public XamlDocument Document { get; }

    /// <summary>
    /// Gets the fragment's element, or <see langword="null"/> when the text it was parsed from held
    /// none.
    /// </summary>
    public XamlElement? Root { get; }

    /// <summary>Gets the document the fragment was lifted from, when that is known.</summary>
    /// <remarks>
    /// What a relative URI inside the fragment — an image's source — was relative to. Nothing here
    /// rewrites one: which attributes hold a URI is a question about what members mean, and this
    /// package cannot answer it.
    /// </remarks>
    public Uri? SourceUri { get; }

    /// <summary>
    /// Gets the namespaces the fragment declares, keyed by prefix, with the default namespace
    /// under an empty key.
    /// </summary>
    public IReadOnlyDictionary<string, string> Namespaces { get; }

    /// <summary>
    /// Gets the namespaces whose content a reader may ignore, as the fragment's source listed them
    /// in <c>mc:Ignorable</c>.
    /// </summary>
    public ImmutableArray<string> IgnorableNamespaces { get; }

    /// <summary>Lifts an element out of its document.</summary>
    /// <remarks>
    /// <para>
    /// The declarations that travel are the ones in scope where the element sits that its text
    /// mentions — every one it mentions, judged from the text rather than the syntax, because one
    /// too many costs a declaration nobody uses and one too few costs markup that does not load.
    /// The default namespace always travels: unprefixed names are written in it.
    /// </para>
    /// <para>
    /// A namespace is carried as ignorable when the element or one of its ancestors lists its
    /// prefix in <c>mc:Ignorable</c>, which is where markup compatibility says the list applies.
    /// </para>
    /// </remarks>
    /// <param name="element">The element to lift. The document it belongs to is not changed.</param>
    /// <returns>The fragment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static XamlFragment From(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        string text = XamlDocumentEditor.Outdent(element, XamlDocumentEditor.IndentOf(element));

        var own = new HashSet<string>(
            element.NamespaceDeclarations.Select(static declaration => declaration.Prefix ?? string.Empty),
            StringComparer.Ordinal);

        var carried = new List<KeyValuePair<string, string>>();

        foreach ((string prefix, string namespaceUri) in InScopeInSourceOrder(element))
        {
            // One the element makes itself is already in its text, and an empty URI undeclares.
            if (own.Contains(prefix) || namespaceUri.Length == 0)
            {
                continue;
            }

            if (prefix.Length == 0 || XamlPrefixUses.IsMentioned(text, prefix))
            {
                carried.Add(new(prefix, namespaceUri));
            }
        }

        // Every prefix the fragment will be able to resolve, and what each resolves to.
        var scope = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach ((string prefix, string namespaceUri) in carried)
        {
            scope[prefix] = namespaceUri;
        }

        foreach (XamlNamespaceDeclaration declaration in element.NamespaceDeclarations)
        {
            scope[declaration.Prefix ?? string.Empty] = declaration.GetNamespaceUri();
        }

        var head = new StringBuilder();

        foreach ((string prefix, string namespaceUri) in carried)
        {
            // Raw attribute text, as the declaration it came from had it; only the quote this
            // writes with has to be escaped.
            head.Append(prefix.Length == 0 ? " xmlns=\"" : $" xmlns:{prefix}=\"")
                .Append(namespaceUri.Replace("\"", "&quot;", StringComparison.Ordinal))
                .Append('"');
        }

        if (IgnorableAttributeOf(element) is null)
        {
            AppendIgnorable(element, text, scope, head);
        }

        // Straight after the element's name, where an author writes them. The name is on the
        // first line, which taking the indentation off the others did not move.
        int at = element.NameSpan.End - element.Span.Start;

        return new XamlFragment(
            XamlDocument.Parse(text.Insert(at, head.ToString())),
            element.Document.Uri);
    }

    /// <summary>Reads a fragment back from its text — what a clipboard holds.</summary>
    /// <remarks>
    /// Whatever the text puts around the element — a declaration, comments, blank lines — is not
    /// part of the fragment. Text copied out of an indented document is left-aligned by the
    /// indentation of the line its end tag stands on.
    /// </remarks>
    /// <param name="text">The text, as <see cref="ToXamlText"/> wrote it or as a person typed it.</param>
    /// <param name="sourceUri">The document the text came from, when the caller knows it.</param>
    /// <returns>
    /// The fragment. Text that holds no element gives one whose <see cref="Root"/> is
    /// <see langword="null"/>; text that does not parse cleanly carries its diagnostics in
    /// <see cref="Document"/>, and <see cref="XamlDocumentEditor.InsertFragment"/> refuses both.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static XamlFragment Parse(string text, Uri? sourceUri = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        var document = XamlDocument.Parse(text);

        if (document.Root is { } root && document.IsWellFormed)
        {
            document = XamlDocument.Parse(XamlDocumentEditor.Outdent(root, EndTagIndentOf(root)));
        }

        return new XamlFragment(document, sourceUri);
    }

    /// <summary>
    /// Writes the fragment as markup that stands on its own: the element with the declarations it
    /// needs on its start tag.
    /// </summary>
    /// <returns>The text, ready for a clipboard or a file.</returns>
    public string ToXamlText() => Root?.GetText() ?? Document.GetText();

    /// <summary>
    /// Lists the declarations in scope at an element in the order its document wrote them, an
    /// inner one taking the place of the outer one it shadows.
    /// </summary>
    /// <remarks>
    /// The namespace context answers which prefix means what, and keeps no order — and the order is
    /// what an author reads, and what the receiving document's root will show.
    /// </remarks>
    private static List<KeyValuePair<string, string>> InScopeInSourceOrder(XamlElement element)
    {
        var declarations = new List<KeyValuePair<string, string>>();

        foreach (XamlElement scope in element.AncestorsAndSelf().OfType<XamlElement>().Reverse())
        {
            foreach (XamlNamespaceDeclaration declaration in scope.NamespaceDeclarations)
            {
                string prefix = declaration.Prefix ?? string.Empty;
                int existing = declarations.FindIndex(entry => string.Equals(entry.Key, prefix, StringComparison.Ordinal));
                var entry = new KeyValuePair<string, string>(prefix, declaration.GetNamespaceUri());

                if (existing < 0)
                {
                    declarations.Add(entry);
                }
                else
                {
                    declarations[existing] = entry;
                }
            }
        }

        return declarations;
    }

    /// <summary>Finds the attribute that lists an element's ignorable prefixes, if it has one.</summary>
    internal static XamlAttribute? IgnorableAttributeOf(XamlElement element) =>
        element.Attributes.FirstOrDefault(static attribute =>
            attribute is not XamlNamespaceDeclaration
            && attribute.IsMarkupCompatibility
            && string.Equals(attribute.Name.LocalName, Ignorable, StringComparison.Ordinal));

    /// <summary>Reads the namespaces an element's <c>mc:Ignorable</c> lists, through its own scope.</summary>
    private static IEnumerable<string> ListedIgnorable(XamlElement element)
    {
        if (IgnorableAttributeOf(element) is not { } attribute)
        {
            yield break;
        }

        foreach (string prefix in attribute.GetValueText().Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.NamespaceContext.LookupNamespace(prefix) is { } namespaceUri)
            {
                yield return namespaceUri;
            }
        }
    }

    private static ImmutableArray<string> IgnorableNamespacesOf(XamlElement root) =>
        [.. ListedIgnorable(root).Distinct(StringComparer.Ordinal)];

    private static ImmutableDictionary<string, string> DeclarationsOf(XamlElement root)
    {
        ImmutableDictionary<string, string>.Builder declarations =
            ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);

        foreach (XamlNamespaceDeclaration declaration in root.NamespaceDeclarations)
        {
            // A prefix bound twice on one element is malformed; the last one written wins, as it
            // does for the namespace context.
            declarations[declaration.Prefix ?? string.Empty] = declaration.GetNamespaceUri();
        }

        return declarations.ToImmutable();
    }

    /// <summary>
    /// Writes <c>mc:Ignorable</c> onto a fragment's start tag for the namespaces its source listed
    /// that it still uses.
    /// </summary>
    private static void AppendIgnorable(
        XamlElement element,
        string text,
        Dictionary<string, string> scope,
        StringBuilder head)
    {
        var listed = new List<string>();

        foreach (XamlElement ancestor in element.AncestorsAndSelf().OfType<XamlElement>())
        {
            foreach (string namespaceUri in ListedIgnorable(ancestor))
            {
                string? prefix = scope
                    .Where(entry => entry.Key.Length > 0
                        && string.Equals(entry.Value, namespaceUri, StringComparison.Ordinal))
                    .Select(static entry => entry.Key)
                    .FirstOrDefault();

                if (prefix is not null && !listed.Contains(prefix, StringComparer.Ordinal))
                {
                    listed.Add(prefix);
                }
            }
        }

        if (listed.Count == 0)
        {
            return;
        }

        string? compatibility = scope
            .Where(static entry => entry.Key.Length > 0
                && string.Equals(entry.Value, XamlNamespaces.MarkupCompatibility, StringComparison.Ordinal))
            .Select(static entry => entry.Key)
            .FirstOrDefault();

        if (compatibility is null)
        {
            compatibility = "mc";

            for (var number = 1; scope.ContainsKey(compatibility) || XamlPrefixUses.IsMentioned(text, compatibility); number++)
            {
                compatibility = "mc" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            head.Append($" xmlns:{compatibility}=\"{XamlNamespaces.MarkupCompatibility}\"");
        }

        head.Append($" {compatibility}:{Ignorable}=\"{string.Join(' ', listed)}\"");
    }

    /// <summary>
    /// Gets the indentation of the line an element's end tag stands on, when it stands there alone.
    /// </summary>
    private static string EndTagIndentOf(XamlElement root)
    {
        if (root.EndTagSpan is not { } end)
        {
            return string.Empty;
        }

        SourceText text = root.Document.SourceText;
        int start = end.Start;

        while (start > root.Span.Start && text[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return start > root.Span.Start && text[start - 1] is '\n' or '\r'
            ? text.GetText(TextSpan.FromBounds(start, end.Start))
            : string.Empty;
    }
}
