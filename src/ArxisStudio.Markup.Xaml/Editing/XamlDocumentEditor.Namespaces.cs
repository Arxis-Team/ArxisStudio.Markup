using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The half of the editor that writes names in namespaces the document may not have declared yet.
/// </summary>
/// <remarks>
/// <para>
/// A tool that puts a control from a library into a form, writes <c>d:DesignWidth</c> on a document
/// that has no design namespace, or drops a user control of the project onto a page has to name
/// something in a namespace — and whether the document can write that name, and with which prefix,
/// depends on what is declared where. Every such tool otherwise writes the same lookup, the same
/// declaration, and the same <c>mc:Ignorable</c> bookkeeping by hand, against spans.
/// </para>
/// <para>
/// A namespace already in scope is used under the prefix the document gave it. One that is not is
/// declared on the root — Avalonia accepts <c>xmlns</c> nowhere else — after the declarations that
/// are already there and laid out the way they are, under a prefix nothing in the document uses.
/// Nothing is ever declared as the default namespace: that would change what every unprefixed name
/// in the document means.
/// </para>
/// <para>
/// Every declaration and the ignorable list are recorded as edits like any other, so they are part
/// of the same change, the same undo entry, and are refused together with it.
/// </para>
/// </remarks>
public sealed partial class XamlDocumentEditor
{
    /// <summary>The local name of the markup-compatibility attribute that lists ignorable prefixes.</summary>
    private const string IgnorableName = "Ignorable";

    /// <summary>Declarations this editor has added to the root, in the order they were asked for.</summary>
    private readonly List<KeyValuePair<string, string>> _declared = [];

    /// <summary>
    /// Where in the recorded changes the root's new declarations are written, once there are any —
    /// one change for all of them, rewritten as they are added.
    /// </summary>
    private int _declarationChange = -1;

    /// <summary>The text the root's <c>mc:Ignorable</c> will have, once this editor has changed it.</summary>
    private string? _ignorable;

    /// <summary>The name of an <c>mc:Ignorable</c> this editor is adding, rather than changing.</summary>
    private XamlQualifiedName? _newIgnorable;

    /// <summary>Where in the recorded changes an existing <c>mc:Ignorable</c> is rewritten.</summary>
    private int _ignorableChange = -1;

    /// <summary>Every prefix the document declares or writes anywhere, read once.</summary>
    private HashSet<string>? _prefixesInUse;

    /// <summary>
    /// Names something the way this document can write it at a given place — an element, or an
    /// attached property's owner — declaring its namespace on the root when nothing in scope binds
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name may come back unprefixed: an element in the default namespace is written without
    /// one, and so is an attached property whose owner is in it, as <c>Grid.Row</c> is. For a name
    /// that must carry a prefix whatever the default namespace is — a directive, a design-time
    /// attribute — use <see cref="QualifyAttribute"/>.
    /// </para>
    /// <para>
    /// Declaring the design namespace makes it ignorable in the same edit, because a <c>d:</c>
    /// attribute a compiler has not been told to skip is a build error. See
    /// <see cref="EnsureIgnorable"/>.
    /// </para>
    /// </remarks>
    /// <param name="scope">
    /// The element the name will be written in or under — the parent of an element about to be
    /// inserted, or the element an attribute is about to be set on.
    /// </param>
    /// <param name="namespaceUri">The namespace, as a declaration would write it — <c>using:App.Controls</c>.</param>
    /// <param name="localName">The local name, dotted for an attached property's owner and member.</param>
    /// <param name="preferredPrefix">
    /// The prefix to use when one has to be declared, or <see langword="null"/> to make one up from
    /// the namespace. A prefix the document already uses for something else gets a number.
    /// </param>
    /// <returns>The name to write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="namespaceUri"/> or <paramref name="localName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scope"/> belongs to a different document.</exception>
    public XamlQualifiedName Qualify(
        XamlElement scope,
        string namespaceUri,
        string localName,
        string? preferredPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(namespaceUri);
        ArgumentException.ThrowIfNullOrEmpty(localName);
        Validate(scope);

        string prefix = PrefixFor(scope, namespaceUri, preferredPrefix, unprefixed: true);

        return new XamlQualifiedName(prefix.Length == 0 ? null : prefix, localName);
    }

    /// <summary>
    /// Names an attribute in a namespace — a directive, a design-time attribute — declaring the
    /// namespace on the root when nothing in scope binds it to a prefix.
    /// </summary>
    /// <remarks>
    /// Always prefixed. An unprefixed attribute is in no namespace at all, whatever the default
    /// namespace is, so a namespace in scope only as the default is declared again under a prefix.
    /// </remarks>
    /// <param name="scope">The element the attribute will be set on.</param>
    /// <param name="namespaceUri">The namespace.</param>
    /// <param name="localName">The attribute's local name.</param>
    /// <param name="preferredPrefix">The prefix to use when one has to be declared.</param>
    /// <returns>The name to write.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="namespaceUri"/> or <paramref name="localName"/> is empty.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="scope"/> belongs to a different document.</exception>
    public XamlQualifiedName QualifyAttribute(
        XamlElement scope,
        string namespaceUri,
        string localName,
        string? preferredPrefix = null)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrEmpty(namespaceUri);
        ArgumentException.ThrowIfNullOrEmpty(localName);
        Validate(scope);

        return new XamlQualifiedName(PrefixFor(scope, namespaceUri, preferredPrefix, unprefixed: false), localName);
    }

    /// <summary>
    /// Makes sure the root lists a namespace in <c>mc:Ignorable</c>, declaring it and the
    /// markup-compatibility namespace where the document has not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An existing list is added to, never rewritten: what it already says, and how, stays. A list
    /// that already names the namespace's prefix leaves the document as it is.
    /// </para>
    /// <para>
    /// A namespace in scope only as the default is declared again under a prefix, because the list
    /// holds prefixes, and the default namespace has none.
    /// </para>
    /// </remarks>
    /// <param name="namespaceUri">The namespace a reader may ignore.</param>
    /// <param name="preferredPrefix">The prefix to use when one has to be declared.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="namespaceUri"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The document has no root element.</exception>
    public XamlDocumentEditor EnsureIgnorable(string namespaceUri, string? preferredPrefix = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(namespaceUri);

        XamlElement root = RootForDeclarations();

        MakeIgnorable(root, PrefixFor(root, namespaceUri, preferredPrefix, unprefixed: false));

        return this;
    }

    /// <summary>
    /// Finds the prefix a namespace is written with at a place, declaring one on the root when
    /// there is none.
    /// </summary>
    /// <param name="scope">Where the name will be written.</param>
    /// <param name="namespaceUri">The namespace.</param>
    /// <param name="preferredPrefix">The prefix to prefer, if the document has it or can take it.</param>
    /// <param name="unprefixed">Whether the default namespace may answer, as it may for an element.</param>
    /// <returns>The prefix, or an empty string for the default namespace.</returns>
    private string PrefixFor(XamlElement scope, string namespaceUri, string? preferredPrefix, bool unprefixed)
    {
        // Fixed by XML itself, and never declared.
        if (string.Equals(namespaceUri, XamlNamespaces.Xml, StringComparison.Ordinal))
        {
            return "xml";
        }

        IReadOnlyDictionary<string, string> inScope = scope.NamespaceContext.GetInScopeDeclarations();

        if (preferredPrefix is { Length: > 0 }
            && inScope.TryGetValue(preferredPrefix, out string? preferred)
            && string.Equals(preferred, namespaceUri, StringComparison.Ordinal))
        {
            return preferredPrefix;
        }

        if (unprefixed
            && inScope.TryGetValue(string.Empty, out string? defaultNamespace)
            && string.Equals(defaultNamespace, namespaceUri, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        // Innermost first, as the scope lists them, so a closer declaration wins.
        foreach ((string prefix, string declared) in inScope)
        {
            if (prefix.Length > 0 && string.Equals(declared, namespaceUri, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        // One this editor has already declared, which every place in the document sees: its prefix
        // was chosen for being used nowhere, so nothing shadows it.
        foreach ((string prefix, string declared) in _declared)
        {
            if (string.Equals(declared, namespaceUri, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        return Declare(namespaceUri, AvailablePrefix(preferredPrefix ?? SuggestPrefix(namespaceUri)));
    }

    /// <summary>Records a declaration on the root, under a prefix already chosen.</summary>
    /// <returns>The prefix.</returns>
    private string Declare(string namespaceUri, string prefix)
    {
        XamlElement root = RootForDeclarations();

        _declared.Add(new(prefix, namespaceUri));
        WriteDeclarations(root);

        // Never one without the other: a design-time attribute a compiler has not been told it may
        // skip is a build error in the project the document belongs to.
        if (string.Equals(namespaceUri, XamlNamespaces.Design, StringComparison.Ordinal))
        {
            MakeIgnorable(root, prefix);
        }

        return prefix;
    }

    /// <summary>Gets the namespace this editor has declared a prefix for, if it has.</summary>
    private string? Declared(string prefix)
    {
        foreach ((string declared, string namespaceUri) in _declared)
        {
            if (string.Equals(declared, prefix, StringComparison.Ordinal))
            {
                return namespaceUri;
            }
        }

        return null;
    }

    /// <summary>Lists a prefix in the root's <c>mc:Ignorable</c>, adding the attribute if it has none.</summary>
    private void MakeIgnorable(XamlElement root, string prefix)
    {
        XamlAttribute? existing = XamlFragment.IgnorableAttributeOf(root);
        string compatibility;

        if (existing is null && UndeclaredIgnorable(root) is { Name.Prefix: { } written } orphan)
        {
            // mc:Ignorable written without its namespace ever being declared. Declaring that prefix
            // for it is what the document meant, and a second list beside it would not parse.
            compatibility = Declared(written) is null
                ? Declare(XamlNamespaces.MarkupCompatibility, written)
                : written;
            existing = orphan;
        }
        else
        {
            compatibility = PrefixFor(root, XamlNamespaces.MarkupCompatibility, "mc", unprefixed: false);
        }

        string listed = _ignorable ?? existing?.GetValueText() ?? string.Empty;

        if (listed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains(prefix, StringComparer.Ordinal))
        {
            return;
        }

        _ignorable = listed.Trim().Length == 0 ? prefix : listed.TrimEnd() + " " + prefix;

        if (existing is null)
        {
            _newIgnorable ??= new XamlQualifiedName(compatibility, IgnorableName);
            WriteDeclarations(root);

            return;
        }

        // The value only, as setting any attribute changes it; whole only when it has no value.
        TextChange change = existing.ValueSpan is { } span && existing.Quote is { } quote
            ? new TextChange(span, Escape(_ignorable, quote))
            : new TextChange(existing.Span, $"{existing.Name}=\"{Escape(_ignorable, '"')}\"");

        Record(ref _ignorableChange, change);
    }

    /// <summary>Finds an <c>…:Ignorable</c> on the root whose prefix nothing declares.</summary>
    private static XamlAttribute? UndeclaredIgnorable(XamlElement root) =>
        root.Attributes.FirstOrDefault(attribute =>
            attribute is not XamlNamespaceDeclaration
            && attribute.Name.Prefix is { } prefix
            && string.Equals(attribute.Name.LocalName, IgnorableName, StringComparison.Ordinal)
            && root.NamespaceContext.LookupNamespace(prefix) is null);

    /// <summary>
    /// Writes every declaration this editor has added, and a new <c>mc:Ignorable</c>, as one
    /// insertion on the root.
    /// </summary>
    /// <remarks>
    /// One change rather than one per declaration, so the order they appear in is the order they
    /// were asked for, and the ignorable list comes after the namespaces it names.
    /// </remarks>
    private void WriteDeclarations(XamlElement root)
    {
        string separator = DeclarationSeparatorFor(root);
        var text = new StringBuilder();

        foreach ((string prefix, string namespaceUri) in _declared)
        {
            text.Append(separator).Append("xmlns:").Append(prefix).Append("=\"").Append(Escape(namespaceUri, '"')).Append('"');
        }

        if (_newIgnorable is { } name && _ignorable is { } listed)
        {
            text.Append(separator).Append(name).Append("=\"").Append(Escape(listed, '"')).Append('"');
        }

        Record(ref _declarationChange, new TextChange(new TextSpan(DeclarationPointFor(root), 0), text.ToString()));
    }

    /// <summary>Records a change, or rewrites the one recorded at the same slot earlier.</summary>
    private void Record(ref int slot, TextChange change)
    {
        if (slot < 0)
        {
            _changes.Add(change);
            slot = _changes.Count - 1;
        }
        else
        {
            _changes[slot] = change;
        }
    }

    /// <summary>The root, which is where a declaration goes.</summary>
    private XamlElement RootForDeclarations() =>
        _document.Root ?? throw new InvalidOperationException(
            "The document has no root element, and a namespace can only be declared on one.");

    /// <summary>After the declarations the root already makes, or after its name when it makes none.</summary>
    private static int DeclarationPointFor(XamlElement root) =>
        root.NamespaceDeclarations.LastOrDefault()?.Span.End ?? root.NameSpan.End;

    /// <summary>
    /// Works out how a new declaration is separated from the one before it: as the root's last
    /// two declarations are, when they are on lines of their own, and as its attributes are
    /// otherwise.
    /// </summary>
    private string DeclarationSeparatorFor(XamlElement root)
    {
        XamlNamespaceDeclaration[] declarations = [.. root.NamespaceDeclarations];

        if (declarations.Length < 2)
        {
            return SeparatorFor(root);
        }

        string between = _document.SourceText.GetText(
            TextSpan.FromBounds(declarations[^2].Span.End, declarations[^1].Span.Start));

        return between.All(char.IsWhiteSpace) && between.Any(static c => c is '\n' or '\r')
            ? between
            : " ";
    }

    /// <summary>
    /// Picks a prefix nothing in the document declares or writes, starting from the one asked for.
    /// </summary>
    private string AvailablePrefix(string wanted)
    {
        string stem = Sanitise(wanted);

        if (!IsTaken(stem))
        {
            return stem;
        }

        for (var number = 1; ; number++)
        {
            string numbered = stem + number.ToString(CultureInfo.InvariantCulture);

            if (!IsTaken(numbered))
            {
                return numbered;
            }
        }
    }

    /// <summary>
    /// Reports whether a prefix is spoken for: declared or written anywhere in the document,
    /// declared by this editor, or reserved by XML.
    /// </summary>
    /// <remarks>
    /// Written as well as declared, because a document that uses a prefix it never declared is
    /// broken in a way a declaration under that prefix would quietly change the meaning of.
    /// </remarks>
    private bool IsTaken(string prefix) =>
        prefix.StartsWith("xml", StringComparison.OrdinalIgnoreCase)
        || PrefixesInUse().Contains(prefix)
        || Declared(prefix) is not null;

    private HashSet<string> PrefixesInUse()
    {
        if (_prefixesInUse is { } known)
        {
            return known;
        }

        var prefixes = new HashSet<string>(StringComparer.Ordinal);

        foreach (XamlElement element in _document.DescendantElements())
        {
            Add(element.Name.Prefix);

            foreach (XamlAttribute attribute in element.Attributes)
            {
                if (attribute is XamlNamespaceDeclaration declaration)
                {
                    Add(declaration.Prefix);

                    continue;
                }

                Add(attribute.Name.Prefix);

                if (attribute.HasValue)
                {
                    AddExtensionPrefixes(attribute.GetValue());
                }
            }
        }

        return _prefixesInUse = prefixes;

        void Add(string? prefix)
        {
            if (prefix is { Length: > 0 })
            {
                prefixes.Add(prefix);
            }
        }

        void AddExtensionPrefixes(XamlValue value)
        {
            if (value is not XamlMarkupExtensionValue extension)
            {
                return;
            }

            Add(extension.TypeName.Prefix);

            foreach (XamlMarkupExtensionArgument argument in extension.Arguments)
            {
                AddExtensionPrefixes(argument.Value);
            }
        }
    }

    /// <summary>Makes up a prefix from a namespace: the last part of a CLR namespace, or a known one.</summary>
    private static string SuggestPrefix(string namespaceUri)
    {
        switch (namespaceUri)
        {
            case XamlNamespaces.Xaml:
                return "x";
            case XamlNamespaces.Design:
                return "d";
            case XamlNamespaces.MarkupCompatibility:
                return "mc";
        }

        const string Using = "using:";
        const string ClrNamespace = "clr-namespace:";

        string? clr = namespaceUri.StartsWith(Using, StringComparison.Ordinal)
            ? namespaceUri[Using.Length..]
            : namespaceUri.StartsWith(ClrNamespace, StringComparison.Ordinal)
                ? namespaceUri[ClrNamespace.Length..].Split(';')[0]
                : null;

        string candidate = clr is not null
            ? clr[(clr.LastIndexOf('.') + 1)..]
            : namespaceUri.TrimEnd('/').Split('/', ':').LastOrDefault(static part => part.Length > 0) ?? string.Empty;

        return candidate.Length == 0 ? "ns" : candidate.ToLowerInvariant();
    }

    /// <summary>Turns a wish into a prefix XML accepts: a letter or underscore first, name characters after.</summary>
    private static string Sanitise(string wanted)
    {
        var builder = new StringBuilder(wanted.Length);

        foreach (char character in wanted)
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '-')
            {
                builder.Append(character);
            }
        }

        string prefix = builder.ToString();

        if (prefix.Length == 0 || !(char.IsLetter(prefix[0]) || prefix[0] == '_'))
        {
            prefix = "ns" + prefix;
        }

        // XML reserves every prefix beginning with these three letters, in any case.
        return prefix.StartsWith("xml", StringComparison.OrdinalIgnoreCase) ? "ns" + prefix : prefix;
    }
}
