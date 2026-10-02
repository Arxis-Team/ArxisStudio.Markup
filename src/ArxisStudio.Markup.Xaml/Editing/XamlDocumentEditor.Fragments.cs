using System;
using System.Collections.Generic;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The half of the editor that puts markup from another document into this one.
/// </summary>
/// <remarks>
/// <para>
/// Pasting, dragging a control from one form to another and extracting part of a form into a user
/// control all come to the same edit: an element written against one document's declarations has
/// to read the same in another's. Its text alone does not — <c>local:Badge</c> names whatever the
/// receiving document binds <c>local</c> to — and inserting it as text is how a paste turns a
/// control into a different one, or into markup that does not load.
/// </para>
/// <para>
/// A namespace the fragment uses is reconciled in the least intrusive way that is right: nothing,
/// when the document binds the same prefix to it where the fragment lands; a declaration on the
/// root under the fragment's own prefix, when nothing in the document uses that prefix; and only
/// when the prefix means something else here, a rename — to the prefix the document already gives
/// that namespace, or to a new one. Renaming is the one step that rewrites what the fragment says,
/// so it is reserved for the case that cannot be done without it.
/// </para>
/// </remarks>
public sealed partial class XamlDocumentEditor
{
    /// <summary>Directives only a document's root may carry, which a fragment stops being.</summary>
    private static readonly string[] RootOnlyDirectives = ["Class", "ClassModifier", "Subclass"];

    /// <summary>The names the document declares, read once, and those fragments have brought in since.</summary>
    private HashSet<string>? _names;

    /// <summary>
    /// Inserts markup from another document as a child of an element, written in this document's
    /// namespaces.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The namespaces the fragment uses are declared on this document's root where they are
    /// missing; where a prefix it uses means something else here, it is renamed everywhere the
    /// syntax says it names a namespace — element and attribute names, markup extensions, values
    /// that are nothing but type names, attached properties in parentheses, style selectors — and
    /// a mention left in any other kind of value is reported. A namespace the fragment's source
    /// marked ignorable is made ignorable here.
    /// </para>
    /// <para>
    /// The inserted element carries none of its declarations, which Avalonia accepts only on a
    /// root, and none of the directives only a root may carry. It is indented to where it lands,
    /// and written with this document's line breaks.
    /// </para>
    /// <para>
    /// Two facts about the fragment refuse the insertion, with a diagnostic and nothing recorded:
    /// a fragment that is not one well-formed element, and one whose unprefixed names are in a
    /// different default namespace from the one where it would land — renaming those would mean
    /// prefixing every unprefixed name in it.
    /// </para>
    /// </remarks>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">
    /// The position among <paramref name="parent"/>'s content children — property elements are
    /// not counted. A value at or beyond the end appends.
    /// </param>
    /// <param name="fragment">The markup to insert.</param>
    /// <param name="names">
    /// What to do with the names inside it. By default only the ones this document already
    /// declares are taken out, so markup moved from another form keeps the names its code refers to.
    /// </param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="fragment"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="parent"/> belongs to a different document.</exception>
    public XamlDocumentEditor InsertFragment(
        XamlElement parent,
        int index,
        XamlFragment fragment,
        XamlDuplicateNames names = XamlDuplicateNames.RemoveConflicting)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(fragment);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Validate(parent);

        if (fragment.Root is not { } root || !fragment.Document.IsWellFormed)
        {
            Report(
                XamlDiagnosticCodes.MalformedFragment,
                "The fragment is not one well-formed element, so nothing was inserted.",
                MarkupDiagnosticSeverity.Error,
                parent.StartTagSpan);

            return this;
        }

        if (fragment.Namespaces.TryGetValue(string.Empty, out string? fragmentDefault)
            && !string.Equals(parent.NamespaceContext.LookupNamespace(null), fragmentDefault, StringComparison.Ordinal)
            && XamlPrefixUses.UsesDefaultNamespace(root))
        {
            Report(
                XamlDiagnosticCodes.FragmentDefaultNamespaceConflict,
                $"The fragment writes its unprefixed names in '{fragmentDefault}', and where it would be " +
                $"inserted they mean '{parent.NamespaceContext.LookupNamespace(null) ?? "no namespace"}'. " +
                "Nothing was inserted.",
                MarkupDiagnosticSeverity.Error,
                parent.StartTagSpan);

            return this;
        }

        // What the inserted text will not carry: the declarations, which go to this root or turn out
        // to be here already; the ignorable list, which goes the same way; what only a root may say;
        // and the names this document cannot take.
        List<XamlAttribute> dropped =
        [
            .. root.NamespaceDeclarations,
            .. RootOnlyDirectives
                .Select(root.GetDirectiveAttribute)
                .OfType<XamlAttribute>(),
        ];

        if (XamlFragment.IgnorableAttributeOf(root) is { } ignorable)
        {
            dropped.Add(ignorable);
        }

        dropped.AddRange(NamesToDrop(root, names));

        TextSpan[] excluded = [.. dropped.Select(static attribute => attribute.Span)];

        var renames = new Dictionary<string, string>(StringComparer.Ordinal);

        // In the order the fragment writes them, which is the order they arrive on this root in.
        foreach (XamlNamespaceDeclaration declaration in root.NamespaceDeclarations)
        {
            string prefix = declaration.Prefix ?? string.Empty;
            string namespaceUri = declaration.GetNamespaceUri();

            if (prefix.Length == 0 || namespaceUri.Length == 0 || !XamlPrefixUses.IsMentioned(root, prefix, excluded))
            {
                continue;
            }

            string? here = parent.NamespaceContext.LookupNamespace(prefix) ?? Declared(prefix);

            if (string.Equals(here, namespaceUri, StringComparison.Ordinal))
            {
                continue;
            }

            if (here is null && !IsTaken(prefix))
            {
                Declare(namespaceUri, prefix);

                continue;
            }

            renames[prefix] = PrefixFor(parent, namespaceUri, prefix, unprefixed: false);
        }

        XamlElement documentRoot = RootForDeclarations();

        foreach (string namespaceUri in fragment.IgnorableNamespaces)
        {
            string? written = fragment.Namespaces
                .Where(entry => entry.Key.Length > 0
                    && string.Equals(entry.Value, namespaceUri, StringComparison.Ordinal)
                    && XamlPrefixUses.IsMentioned(root, entry.Key, excluded))
                .Select(static entry => entry.Key)
                .FirstOrDefault();

            if (written is not null)
            {
                MakeIgnorable(
                    documentRoot,
                    PrefixFor(documentRoot, namespaceUri, renames.GetValueOrDefault(written, written), unprefixed: false));
            }
        }

        XamlElement rewritten = Rewrite(root, dropped, excluded, renames, fragment.Namespaces);

        foreach (string prefix in renames.Keys.Where(prefix => MentionedInValues(rewritten, prefix)))
        {
            Report(
                XamlDiagnosticCodes.FragmentPrefixLeftAsWritten,
                $"The fragment's prefix '{prefix}' was renamed to '{renames[prefix]}' wherever it names a " +
                $"namespace, and is still mentioned in a value not read as a name, which was left as " +
                $"written. Here '{prefix}' means " +
                $"'{parent.NamespaceContext.LookupNamespace(prefix) ?? Declared(prefix) ?? "nothing"}'.",
                MarkupDiagnosticSeverity.Warning,
                parent.StartTagSpan);
        }

        string text = WithLineBreaks(Reindent(rewritten, IndentForContent(parent, index)), NewLineFor(parent));

        return InsertElement(parent, index, text);
    }

    /// <summary>
    /// Writes a fragment's element without what it leaves behind, and with its prefixes renamed.
    /// </summary>
    /// <returns>The element as it will be inserted, in a document of its own.</returns>
    private static XamlElement Rewrite(
        XamlElement root,
        List<XamlAttribute> dropped,
        TextSpan[] excluded,
        Dictionary<string, string> renames,
        IReadOnlyDictionary<string, string> namespaces)
    {
        XamlDocumentEditor editor = root.Document.Edit();

        foreach (XamlAttribute attribute in dropped)
        {
            if (attribute.Parent is XamlElement owner)
            {
                editor.RemoveAttribute(owner, attribute.Name);
            }
        }

        foreach ((string from, string to) in renames)
        {
            foreach (TextSpan span in XamlPrefixUses.Find(root, from, namespaces[from]))
            {
                // Inside something being taken out there is nothing left to rename.
                if (!excluded.Any(removed => removed.Contains(span)))
                {
                    editor.Replace(span, to);
                }
            }
        }

        // The declarations are gone, so its prefixes resolve to nothing here — which is only
        // ever read for its text and its shape.
        return editor.Apply().Root!;
    }

    /// <summary>Picks out the name attributes a fragment cannot keep in this document.</summary>
    private List<XamlAttribute> NamesToDrop(XamlElement root, XamlDuplicateNames names)
    {
        var dropped = new List<XamlAttribute>();

        if (names == XamlDuplicateNames.Keep)
        {
            return dropped;
        }

        HashSet<string> taken = NamesInUse();

        foreach (XamlElement element in root.DescendantElements().Prepend(root))
        {
            XamlAttribute?[] named =
            [
                element.GetDirectiveAttribute(XamlDirectives.Name),
                element.GetAttribute(XamlQualifiedName.Unprefixed("Name")),
            ];

            foreach (XamlAttribute attribute in named.OfType<XamlAttribute>())
            {
                // Taken as the first one keeps it, so a second fragment in the same editor that
                // brings the same name loses its copy.
                if (names == XamlDuplicateNames.Remove || !taken.Add(attribute.GetValueText()))
                {
                    dropped.Add(attribute);
                }
            }
        }

        return dropped;
    }

    private HashSet<string> NamesInUse()
    {
        if (_names is null)
        {
            _names = new HashSet<string>(StringComparer.Ordinal);

            foreach (XamlElement element in _document.DescendantElements())
            {
                if (element.Identity is { } name)
                {
                    _names.Add(name);
                }
            }
        }

        return _names;
    }

    /// <summary>Reports whether a prefix is still mentioned in any attribute value of an element.</summary>
    private static bool MentionedInValues(XamlElement element, string prefix) =>
        element.DescendantElements()
            .Prepend(element)
            .SelectMany(static current => current.Attributes)
            .Any(attribute => attribute is not XamlNamespaceDeclaration
                && XamlPrefixUses.IsMentioned(attribute.GetValueText(), prefix));

    /// <summary>
    /// Gets the indentation markup inserted at a position should continue its lines with: that of
    /// the sibling it lands beside, or one step in from the parent when it has none.
    /// </summary>
    private static string IndentForContent(XamlElement parent, int index)
    {
        XamlElement[] children = [.. parent.ContentElements];

        XamlElement? beside = children.Length > 0
            ? children[Math.Min(index, children.Length - 1)]
            : parent.MemberElements.LastOrDefault();

        return beside is not null ? IndentOf(beside) : IndentOf(parent) + StepFor(parent);
    }

    /// <summary>Writes every line break in the text as the document writes its own.</summary>
    /// <remarks>
    /// Inside a value as well: an XML reader reads every kind of line break as a line feed, so no
    /// value means anything different for it, and a file with two kinds of line ending is the kind
    /// of change nobody asked for.
    /// </remarks>
    private static string WithLineBreaks(string text, string newLine) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\n", newLine, StringComparison.Ordinal);

    private void Report(string code, string message, MarkupDiagnosticSeverity severity, TextSpan span) =>
        _diagnostics.Add(MarkupDiagnostic.Parse(code, message, severity, _document.Uri, span));
}
