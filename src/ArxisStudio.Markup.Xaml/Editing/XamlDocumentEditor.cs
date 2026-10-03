using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Accumulates structured edits to a document and turns them into the smallest text changes
/// that express them.
/// </summary>
/// <remarks>
/// <para>
/// Every edit is expressed as a <see cref="TextChange"/> over the original snapshot, touching
/// only the characters that actually change. Setting an attribute's value replaces the text
/// between its quotes and nothing else — not the quote characters, not the spacing around the
/// equals sign, not the other attributes, not the element's children. Preserving unrelated
/// source is not something the editor tries to do afterwards; it is what the changes are.
/// </para>
/// <para>
/// Edits are computed against the document the editor was opened on and applied all at once.
/// Nodes from a different document are rejected: their spans point into different text, so
/// using them would corrupt this one. Markup <em>from</em> a different document arrives as a
/// <see cref="XamlFragment"/>, which carries the namespaces its names are written in.
/// </para>
/// </remarks>
public sealed partial class XamlDocumentEditor
{
    /// <summary>A name no real document uses, for the wrapper a copied fragment is parsed in.</summary>
    private const string FragmentName = "ArxisStudioMarkupFragment";

    private readonly XamlDocument _document;
    private readonly List<TextChange> _changes = [];
    private readonly List<MarkupDiagnostic> _diagnostics = [];

    /// <summary>
    /// Elements that held no children when this editor gave them their first, and the change that did:
    /// a later child put into the same element joins that change.
    /// </summary>
    private Dictionary<XamlElement, FirstContent>? _firstContent;

    internal XamlDocumentEditor(XamlDocument document) => _document = document;

    /// <summary>Gets the document these edits are computed against.</summary>
    public XamlDocument Document => _document;

    /// <summary>Gets a value indicating whether any edit has been recorded.</summary>
    public bool HasChanges => _changes.Count > 0;

    /// <summary>
    /// Gets the diagnostics raised while recording edits.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two refusals, said in a form a tool can route on: a node from another document, and edits
    /// that overlap. They are also thrown, because both are the caller's mistake rather than the
    /// document's and an edit that silently did nothing would be worse — this is the same fact with
    /// a code and a span attached, for a host that catches the exception and wants to show
    /// something better than its message.
    /// </para>
    /// <para>
    /// What <see cref="InsertFragment"/> reports is the other kind: facts about the markup being
    /// inserted, which is a clipboard's or another file's rather than the caller's. Those are
    /// reported here and not thrown.
    /// </para>
    /// </remarks>
    public ImmutableArray<MarkupDiagnostic> Diagnostics => [.. _diagnostics];

    /// <summary>
    /// Sets an attribute's value, adding the attribute if the element does not have it.
    /// </summary>
    /// <remarks>
    /// An existing attribute keeps its quote character and its position in the tag; only the
    /// text between the quotes changes. A new one is appended after the last attribute, using
    /// the whitespace that already separates the existing attributes so it matches the tag's
    /// layout instead of imposing one.
    /// </remarks>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as it should be written, prefix included.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="element"/> belongs to a different document.</exception>
    public XamlDocumentEditor SetAttribute(XamlElement element, XamlQualifiedName name, XamlValue value)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(value);
        Validate(element);

        string text = value.ToXamlText();
        XamlAttribute? existing = element.GetAttribute(name);

        if (existing is null)
        {
            return Insert(InsertionPointFor(element), $"{SeparatorFor(element)}{name}=\"{Escape(text, '"')}\"");
        }

        if (existing.ValueSpan is { } valueSpan && existing.Quote is { } quote)
        {
            // The minimal change: the value only. Everything else about how the attribute was
            // written survives untouched.
            return Replace(valueSpan, Escape(text, quote));
        }

        // The attribute is malformed and has no value to replace, so it is rewritten whole.
        return Replace(existing.Span, $"{name}=\"{Escape(text, '"')}\"");
    }

    /// <summary>
    /// Sets an attribute from raw attribute text.
    /// </summary>
    /// <remarks>
    /// The text is read exactly as reading an attribute reads it, so <c>{Binding Name}</c> sets
    /// a binding and <c>{}{literal}</c> sets a literal brace. Anything else would make writing
    /// a value back differ from reading it. To set text that must stay literal whatever it
    /// looks like, pass <see cref="XamlLiteralValue"/> instead.
    /// </remarks>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as it should be written.</param>
    /// <param name="text">The raw attribute text.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public XamlDocumentEditor SetAttribute(XamlElement element, XamlQualifiedName name, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return SetAttribute(element, name, XamlValue.Parse(text));
    }

    /// <summary>
    /// Removes an attribute, along with the whitespace that separated it from what came before.
    /// </summary>
    /// <remarks>
    /// Taking the leading whitespace with it is what stops removal from leaving a double space
    /// or a dangling indented blank in the middle of a tag.
    /// </remarks>
    /// <param name="element">The element to change.</param>
    /// <param name="name">The attribute name as written.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="element"/> belongs to a different document.</exception>
    public XamlDocumentEditor RemoveAttribute(XamlElement element, XamlQualifiedName name)
    {
        ArgumentNullException.ThrowIfNull(element);
        Validate(element);

        XamlAttribute? attribute = element.GetAttribute(name);

        if (attribute is null)
        {
            return this;
        }

        int start = attribute.Span.Start;

        while (start > element.NameSpan.End && char.IsWhiteSpace(_document.SourceText[start - 1]))
        {
            start--;
        }

        return Replace(TextSpan.FromBounds(start, attribute.Span.End), string.Empty);
    }

    /// <summary>
    /// Removes an element, and the line it sat on if it had that line to itself.
    /// </summary>
    /// <remarks>
    /// Removing only the element's own span would leave its indentation behind as a blank
    /// line, which is a change to the document's shape that nobody asked for. Taking the whole
    /// line when the element owns it leaves the surrounding text as if it had never been there.
    /// </remarks>
    /// <param name="element">The element to remove.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="element"/> belongs to a different document.</exception>
    public XamlDocumentEditor RemoveElement(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Validate(element);

        return Replace(SpanToRemoveFor(element), string.Empty);
    }

    /// <summary>
    /// Inserts XAML as a child of an element, at a position among its existing child elements.
    /// </summary>
    /// <remarks>
    /// An element with no children takes what one editor puts into it in the order it was put, each
    /// child on a line of its own where the element is laid out on lines — the index has nothing to
    /// count there. Pasting two controls into an empty panel is that: the second goes after the first,
    /// inside the same opening of the element.
    /// </remarks>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">
    /// The position among <paramref name="parent"/>'s content children — property elements are
    /// not counted. A value at or beyond the end appends.
    /// </param>
    /// <param name="xaml">The XAML text to insert.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="xaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="parent"/> belongs to a different document.</exception>
    public XamlDocumentEditor InsertElement(XamlElement parent, int index, string xaml)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Validate(parent);

        if (_firstContent?.GetValueOrDefault(parent) is { } first)
        {
            return JoinFirstContent(first, xaml);
        }

        if (parent.IsEmpty)
        {
            return OpenForChildren(parent, xaml);
        }

        (int position, string prefix, string suffix) = ContentInsertionPointFor(parent, index);

        // The first child of an element holding nothing brings the line its end tag goes back to; a second
        // one recorded beside it would bring another, and leave a blank line between the two.
        if (suffix.Length > 0 && !parent.ContentElements.Any())
        {
            return FirstChild(parent, new TextSpan(position, 0), prefix, prefix, suffix, xaml);
        }

        return Insert(position, prefix + xaml + suffix);
    }

    /// <summary>
    /// Records the change that gives an element without children its first, remembering how it is
    /// written so that a later child can join it.
    /// </summary>
    /// <param name="parent">The element.</param>
    /// <param name="span">What the change replaces.</param>
    /// <param name="head">What goes before the first child.</param>
    /// <param name="between">What goes between two children.</param>
    /// <param name="tail">What goes after the last child.</param>
    /// <param name="xaml">The first child.</param>
    private XamlDocumentEditor FirstChild(
        XamlElement parent,
        TextSpan span,
        string head,
        string between,
        string tail,
        string xaml)
    {
        (_firstContent ??= new Dictionary<XamlElement, FirstContent>(ReferenceEqualityComparer.Instance))[parent] =
            new FirstContent(_changes.Count, span, head, between, tail, [xaml]);

        return Replace(span, head + xaml + tail);
    }

    /// <summary>Puts another child after the ones this editor already gave an element without children.</summary>
    private XamlDocumentEditor JoinFirstContent(FirstContent first, string xaml)
    {
        first.Children.Add(xaml);
        _changes[first.Change] = new TextChange(
            first.Span,
            first.Head + string.Join(first.Between, first.Children) + first.Tail);

        return this;
    }

    /// <summary>
    /// Reports whether an element's first child goes on a line of its own: the element begins a line, in
    /// a document written on lines at all.
    /// </summary>
    /// <remarks>
    /// A document written on one line stays on one line. The root of one begins its line too, and
    /// breaking it open would change the layout of a file in which nothing asked for lines.
    /// </remarks>
    private bool LaysOutOnLines(XamlElement element) =>
        StartsLine(element) && _document.SourceText.Lines.Count > 1;

    /// <summary>
    /// Gives a self-closing element a content region, and puts the first child into it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This used to be refused, on the grounds that where to put content in an element that has
    /// nowhere to put it was a guess. It is not a guess: an element with no content has exactly one
    /// lossless expansion, <c>&lt;Grid /&gt;</c> to <c>&lt;Grid&gt;&lt;/Grid&gt;</c>, and nothing
    /// about the document's meaning changes on the way. What the refusal actually bought was every
    /// caller writing the same text surgery — find the slash, rewrite the tag, close it by name —
    /// against spans, which is the one thing an editor exists to keep them away from.
    /// </para>
    /// <para>
    /// It is not a rare shape either. A designer that writes <c>&lt;Grid Width="200" /&gt;</c> and
    /// then drops a button into it meets it immediately, and so does anybody whose file was written
    /// by hand: an empty container is written self-closing by every convention there is.
    /// </para>
    /// <para>
    /// The whitespace before the slash goes with it, so <c>&lt;Grid /&gt;</c> closes as
    /// <c>&lt;Grid&gt;</c> rather than <c>&lt;Grid &gt;</c>. The name is written back exactly as the
    /// document wrote it, prefix and all, because a closing tag that renames its element is not the
    /// same document.
    /// </para>
    /// </remarks>
    private XamlDocumentEditor OpenAndInsert(XamlElement parent, string xaml) =>
        Replace(OpeningOf(parent), $">{xaml}</{parent.Name}>");

    /// <summary>
    /// Opens a self-closing element for its first child, as <see cref="OpenAndInsert"/> does, so that a
    /// later child put by this editor joins the same opening.
    /// </summary>
    /// <remarks>
    /// An element that begins a line takes its children on lines of their own, one step in from it, and
    /// its end tag back on a line at its own indentation.
    /// </remarks>
    private XamlDocumentEditor OpenForChildren(XamlElement parent, string xaml)
    {
        string close = $"</{parent.Name}>";

        if (!LaysOutOnLines(parent))
        {
            return FirstChild(parent, OpeningOf(parent), ">", string.Empty, close, xaml);
        }

        string newLine = NewLineFor(parent);
        string indent = IndentOf(parent);
        string inner = newLine + indent + StepFor(parent);

        return FirstChild(parent, OpeningOf(parent), ">" + inner, inner, newLine + indent + close, xaml);
    }

    /// <summary>The slash that closes a self-closing element, with the whitespace before it.</summary>
    private TextSpan OpeningOf(XamlElement parent)
    {
        int end = parent.StartTagSpan.End;
        int start = end - 2;

        while (start > parent.NameSpan.End && char.IsWhiteSpace(_document.SourceText[start - 1]))
        {
            start--;
        }

        return TextSpan.FromBounds(start, end);
    }

    /// <summary>
    /// The change that gave an element without children its first: where it stands among the recorded
    /// changes, what it replaces, and the text around and between the children it holds.
    /// </summary>
    private sealed record FirstContent(
        int Change,
        TextSpan Span,
        string Head,
        string Between,
        string Tail,
        List<string> Children);

    /// <summary>Inserts a copy of an element as a child of another.</summary>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">The position among the parent's content children.</param>
    /// <param name="element">The element whose text is inserted.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    public XamlDocumentEditor InsertElement(XamlElement parent, int index, XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return InsertElement(parent, index, element.GetText());
    }

    /// <summary>
    /// Puts a copy of an element straight after it, among the same siblings.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copy arrives written exactly as the original was, apart from the names, which are
    /// taken out unless the caller says otherwise — see <see cref="XamlDuplicateNames"/> for why
    /// that is the default.
    /// </para>
    /// <para>
    /// <c>x:Key</c> is copied as it stands, and a resource dictionary refuses a second entry
    /// under the same key just as a name scope refuses a second name. Duplicating a keyed resource
    /// therefore produces a document that will not load until the caller gives the copy a key of
    /// its own — which key is a question about the tool's naming, not about copying.
    /// </para>
    /// </remarks>
    /// <param name="element">The element to copy.</param>
    /// <param name="names">What to do with the names inside the copy.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="element"/> belongs to a different document, is the root and so has no
    /// siblings to be copied among, or is a property element, which is a member of its parent
    /// rather than one of a number of things beside it.
    /// </exception>
    public XamlDocumentEditor DuplicateElement(
        XamlElement element,
        XamlDuplicateNames names = XamlDuplicateNames.Remove)
    {
        ArgumentNullException.ThrowIfNull(element);
        Validate(element);

        if (element.Parent is not XamlElement parent)
        {
            throw new InvalidOperationException(
                $"Element '{element.Name}' is the root of its document and has no siblings to be " +
                "copied among.");
        }

        if (element.IsPropertyElementSyntax)
        {
            // A second <Grid.ColumnDefinitions> is not a copy of anything: an element has each of
            // its members once, and the position a copy would take does not exist.
            throw new InvalidOperationException(
                $"Element '{element.Name}' is a property element. Duplicate what it contains, or " +
                "the element that declares it.");
        }

        // RemoveConflicting is Remove here: every name in a copy is already declared, by the
        // original the copy stands beside.
        string text = names == XamlDuplicateNames.Keep ? element.GetText() : Anonymous(element);

        return InsertElement(parent, element.IndexInContent + 1, text);
    }

    /// <summary>
    /// Renders an element's text with every name in it taken out.
    /// </summary>
    /// <remarks>
    /// Parsed inside a wrapper that declares every namespace the element had in scope, because a
    /// fragment lifted out of its document carries none of them — and an attribute whose prefix is
    /// bound to nothing is not the directive it looks like. Assuming the prefix is <c>x</c> would
    /// have worked for most documents and silently left the names in the rest.
    /// </remarks>
    private static string Anonymous(XamlElement element)
    {
        var copy = XamlDocument.Parse(
            $"<{FragmentName}{Declarations(element)}>{element.GetText()}</{FragmentName}>");

        XamlDocumentEditor editor = copy.Edit();

        foreach (XamlElement node in copy.DescendantElements())
        {
            if (node.GetDirectiveAttribute(XamlDirectives.Name) is { } directive)
            {
                editor.RemoveAttribute(node, directive.Name);
            }

            if (node.GetAttribute(XamlQualifiedName.Unprefixed("Name")) is { } attribute)
            {
                editor.RemoveAttribute(node, attribute.Name);
            }
        }

        return editor.Apply().Root!.ContentElements.First().GetText();
    }

    /// <summary>Writes an element's in-scope namespaces as declarations for a wrapper to carry.</summary>
    private static string Declarations(XamlElement element)
    {
        var text = new StringBuilder();

        foreach ((string prefix, string uri) in element.NamespaceContext.GetInScopeDeclarations())
        {
            text.Append(prefix.Length == 0 ? " xmlns=\"" : $" xmlns:{prefix}=\"")
                .Append(uri)
                .Append('"');
        }

        // The XAML namespace even where the document never declared it, so that the wrapper is
        // always able to say what an x:Name is — an element that carries none is unaffected.
        if (element.NamespaceContext.LookupPrefix(XamlNamespaces.Xaml) is null)
        {
            text.Append($" xmlns:x=\"{XamlNamespaces.Xaml}\"");
        }

        return text.ToString();
    }

    /// <summary>
    /// Moves an element to a position under a new parent.
    /// </summary>
    /// <remarks>
    /// Expressed as a removal and an insertion of the element's exact text, so the element
    /// arrives at its destination written precisely as it was — attributes, children, comments
    /// and all.
    /// </remarks>
    /// <param name="element">The element to move.</param>
    /// <param name="newParent">The element to move it under.</param>
    /// <param name="index">The position among the new parent's content children.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="newParent"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// A node belongs to a different document, or the move would place an element inside itself.
    /// </exception>
    public XamlDocumentEditor MoveElement(XamlElement element, XamlElement newParent, int index)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(newParent);
        Validate(element);
        Validate(newParent);

        if (ReferenceEquals(element, newParent) || newParent.AncestorsAndSelf().Contains(element))
        {
            throw new InvalidOperationException(
                $"Element '{element.Name}' cannot be moved inside itself.");
        }

        string text = element.GetText();

        RemoveElement(element);

        return InsertElement(newParent, index, text);
    }

    /// <summary>
    /// Replaces an element with other XAML, in place.
    /// </summary>
    /// <remarks>
    /// One change over the element's own span, which is what makes this different from removing
    /// and inserting: the element's position among its siblings, the whitespace on either side of
    /// it and everything else on its line are not part of the change and cannot be disturbed by
    /// it. Turning a <c>Button</c> into a <c>ToggleButton</c> is one edit, not two.
    /// </remarks>
    /// <param name="element">The element to replace.</param>
    /// <param name="xaml">The XAML to put in its place.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="xaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="element"/> belongs to a different document.</exception>
    public XamlDocumentEditor ReplaceElement(XamlElement element, string xaml)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(xaml);
        Validate(element);

        return Replace(element.Span, xaml);
    }

    /// <summary>Replaces an element with a copy of another.</summary>
    /// <param name="element">The element to replace.</param>
    /// <param name="replacement">The element whose text takes its place.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="replacement"/> is <see langword="null"/>.</exception>
    public XamlDocumentEditor ReplaceElement(XamlElement element, XamlElement replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement);

        return ReplaceElement(element, replacement.GetText());
    }

    /// <summary>
    /// Puts an element inside a new one, written where it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The wrapper is given as markup with a start and an end tag — <c>&lt;Border
    /// Padding="8"&gt;&lt;/Border&gt;</c> — and the element moves in between them, indented one
    /// level deeper. Everything it was written with comes along unchanged, because what moves is
    /// its text.
    /// </para>
    /// <para>
    /// One level is measured from the document rather than assumed: the difference between this
    /// element's indentation and its parent's is what the file already uses, and matching it is
    /// the difference between an edit that reads as part of the file and one that reads as a
    /// machine's.
    /// </para>
    /// </remarks>
    /// <param name="element">The element to wrap.</param>
    /// <param name="wrapperXaml">The wrapper, as markup with somewhere to put content.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="wrapperXaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="element"/> belongs to a different document, or the wrapper is not a single
    /// element with a start and an end tag.
    /// </exception>
    public XamlDocumentEditor WrapElement(XamlElement element, string wrapperXaml)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(wrapperXaml);
        Validate(element);

        return Wrap([element], WrapperTags(wrapperXaml));
    }

    /// <summary>
    /// Puts several siblings inside one new element, written where the first of them stood.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a designer calls grouping: three controls selected and put into a panel together. The
    /// wrapper takes the place of the first of them in document order — whatever order they were
    /// named in, because the order a selection was made in is not an order the markup has — and they
    /// move in one after another in that order, one level deeper, each written exactly as it was. The
    /// others leave their places as <see cref="RemoveElement"/> leaves one, so a sibling that was not
    /// named stays where it is: one that stood between them ends up after the wrapper.
    /// </para>
    /// <para>
    /// One element is wrapped as <see cref="WrapElement"/> wraps it. Siblings that stand next to each
    /// other on lines of their own come back character for character when the wrapper is unwrapped.
    /// </para>
    /// <para>
    /// The siblings must share a parent — one wrapper stands in one place, and gathering an element
    /// out of another would be a move nobody asked for — and none of them may be a property element,
    /// which is a member of its parent rather than a thing beside its siblings.
    /// </para>
    /// </remarks>
    /// <param name="elements">The siblings to wrap, in any order; one named twice is wrapped once.</param>
    /// <param name="wrapperXaml">The wrapper, as markup with somewhere to put content.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="elements"/>, an element in it or <paramref name="wrapperXaml"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="elements"/> names no element.</exception>
    /// <exception cref="InvalidOperationException">
    /// An element belongs to a different document, the elements do not share a parent, one of them is a
    /// property element, or the wrapper is not a single element with a start and an end tag.
    /// </exception>
    public XamlDocumentEditor WrapElements(IEnumerable<XamlElement> elements, string wrapperXaml)
    {
        ArgumentNullException.ThrowIfNull(elements);
        ArgumentNullException.ThrowIfNull(wrapperXaml);

        XamlElement[] given = [.. elements];

        if (given.Length == 0)
        {
            throw new ArgumentException("There is nothing to wrap: no element was named.", nameof(elements));
        }

        foreach (XamlElement element in given)
        {
            ArgumentNullException.ThrowIfNull(element, nameof(elements));
            Validate(element);
        }

        XamlElement[] ordered = [.. given.Distinct().OrderBy(static element => element.Span.Start)];
        XamlElement first = ordered[0];

        if (ordered.FirstOrDefault(element => !ReferenceEquals(element.Parent, first.Parent)) is { } stray)
        {
            throw new InvalidOperationException(
                $"Element '{stray.Name}' does not share a parent with '{first.Name}'. Siblings are wrapped " +
                "together; one wrapper stands in one place.");
        }

        if (ordered.FirstOrDefault(static element => element.IsPropertyElementSyntax) is { } member)
        {
            throw new InvalidOperationException(
                $"Element '{member.Name}' is a property element, a member of its parent rather than a " +
                "thing beside its siblings, and is not wrapped with them.");
        }

        return Wrap(ordered, WrapperTags(wrapperXaml));
    }

    /// <summary>
    /// Writes siblings, already in document order, inside a wrapper in place of the first of them, and
    /// takes the others out of their places.
    /// </summary>
    private XamlDocumentEditor Wrap(XamlElement[] ordered, (string Opening, string Closing) wrapper)
    {
        XamlElement first = ordered[0];

        string indent = IndentOf(first);
        string step = StepFor(first);
        string line = NewLineFor(first);
        string inner = line + indent + step;

        Replace(
            first.Span,
            wrapper.Opening + inner + string.Join(inner, ordered.Select(element => Reindent(element, step))) +
            line + indent + wrapper.Closing);

        foreach (XamlElement other in ordered.Skip(1))
        {
            RemoveElement(other);
        }

        return this;
    }

    /// <summary>Reads a wrapper's start tag and end tag, as it writes them.</summary>
    /// <exception cref="InvalidOperationException">The wrapper is not a single element with a start and an end tag.</exception>
    private static (string Opening, string Closing) WrapperTags(string wrapperXaml)
    {
        var wrapper = XamlDocument.Parse(wrapperXaml);

        if (wrapper.Root is not { } root || root.IsEmpty || root.EndTagSpan is not { } endTag)
        {
            throw new InvalidOperationException(
                "A wrapper must be a single element with a start and an end tag, so that there is " +
                $"somewhere to put what is being wrapped. '{wrapperXaml}' is not.");
        }

        return (
            wrapper.SourceText.GetText(TextSpan.FromBounds(root.Span.Start, root.StartTagSpan.End)),
            wrapper.SourceText.GetText(TextSpan.FromBounds(endTag.Start, root.Span.End)));
    }

    /// <summary>
    /// Replaces an element with what it contains.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The children move out one level, indented to match where their wrapper stood. An element
    /// with no child elements is simply removed: unwrapping an empty wrapper leaves nothing, and
    /// that is the only thing it can mean.
    /// </para>
    /// <para>
    /// Property elements are not children in this sense and do not come out. A
    /// <c>&lt;Grid.ColumnDefinitions&gt;</c> is a member of the grid it is written inside, and
    /// promoting it to stand beside its former siblings would produce markup that means nothing
    /// and does not parse where it lands.
    /// </para>
    /// <para>
    /// Whether the slot the wrapper occupied will take more than one child is a question about
    /// what the members mean, which this package deliberately cannot answer. Unwrapping several
    /// children into a single-valued slot produces markup that the loader reports when it tries
    /// to build it.
    /// </para>
    /// </remarks>
    /// <param name="element">The element to unwrap.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="element"/> belongs to a different document.</exception>
    public XamlDocumentEditor UnwrapElement(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        Validate(element);

        XamlElement[] children = [.. element.ContentElements];

        if (children.Length == 0)
        {
            return RemoveElement(element);
        }

        string step = StepFor(children[0]);
        string line = NewLineFor(element);
        string separator = line + IndentOf(element);

        return Replace(
            element.Span,
            string.Join(separator, children.Select(child => Outdent(child, step))));
    }

    /// <summary>Gets the text changes these edits amount to, ordered and non-overlapping.</summary>
    /// <remarks>
    /// An insertion at the point where a replacement begins goes in front of it, whichever was
    /// recorded first. Both describe that point, and "before the text that is being replaced" is
    /// the only reading in which neither swallows the other — it is what lets a namespace be
    /// declared after the last attribute of a self-closing root while the same editor opens that
    /// root to put content in it. Insertions at one point keep the order they were recorded in.
    /// </remarks>
    /// <returns>The changes, ready to apply to the document's snapshot.</returns>
    /// <exception cref="InvalidOperationException">Two edits would change overlapping regions.</exception>
    public ImmutableArray<TextChange> GetTextChanges()
    {
        TextChange[] ordered =
        [
            .. _changes
                .OrderBy(static change => change.Span.Start)
                .ThenBy(static change => change.Span.Length > 0),
        ];

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Span.Start < ordered[index - 1].Span.End)
            {
                string message =
                    $"Two edits change overlapping regions of the document ({ordered[index - 1].Span} " +
                    $"and {ordered[index].Span}). Apply them in separate operations.";

                _diagnostics.Add(MarkupDiagnostic.Parse(
                    XamlDiagnosticCodes.ConflictingEdits,
                    message,
                    MarkupDiagnosticSeverity.Error,
                    _document.Uri,
                    ordered[index].Span));

                throw new InvalidOperationException(message);
            }
        }

        return [.. ordered];
    }

    /// <summary>Applies the edits, producing a new document.</summary>
    /// <remarks>
    /// The result is reparsed from the changed text, so every node in it has spans that match
    /// what the document now says. The document this editor was opened on is unaffected.
    /// </remarks>
    /// <returns>The new document, or the original one when nothing was recorded.</returns>
    /// <exception cref="InvalidOperationException">Two edits would change overlapping regions.</exception>
    public XamlDocument Apply()
    {
        ImmutableArray<TextChange> changes = GetTextChanges();

        if (changes.IsEmpty)
        {
            return _document;
        }

        return XamlDocument.Parse(
            _document.SourceText.WithChanges(changes),
            new XamlParseOptions { DocumentUri = _document.Uri });
    }

    private XamlDocumentEditor Replace(TextSpan span, string text)
    {
        _changes.Add(new TextChange(span, text));

        return this;
    }

    private XamlDocumentEditor Insert(int position, string text) =>
        Replace(new TextSpan(position, 0), text);

    /// <summary>Rejects nodes that came from a different parse of a different text.</summary>
    private void Validate(XamlSyntaxNode node)
    {
        if (ReferenceEquals(node.Document, _document))
        {
            return;
        }

        string message = $"{node} belongs to a different document. Its spans point into different " +
            "text, so editing with it would corrupt this one. Find the node again in this document.";

        _diagnostics.Add(MarkupDiagnostic.Parse(
            XamlDiagnosticCodes.ForeignNode, message, MarkupDiagnosticSeverity.Error, _document.Uri));

        throw new InvalidOperationException(message);
    }

    /// <summary>Finds where a new attribute goes: after the last one, or after the element name.</summary>
    private static int InsertionPointFor(XamlElement element) =>
        element.Attributes.IsEmpty ? element.NameSpan.End : element.Attributes[^1].Span.End;

    /// <summary>
    /// Works out how a new attribute should be separated from the one before it.
    /// </summary>
    /// <remarks>
    /// A tag that puts each attribute on its own line gets the same treatment for the new one,
    /// indentation included: that is the difference between an edit that reads as part of the
    /// file and one that reads as a machine's. Only line-broken separators are copied — the
    /// incidental double space between two attributes on one line is not a layout decision
    /// worth propagating, so a single space is used instead.
    /// </remarks>
    private string SeparatorFor(XamlElement element)
    {
        if (element.Attributes.Length < 2)
        {
            return " ";
        }

        TextSpan between = TextSpan.FromBounds(
            element.Attributes[^2].Span.End,
            element.Attributes[^1].Span.Start);

        string separator = _document.SourceText.GetText(between);

        return separator.All(char.IsWhiteSpace) && separator.Any(static c => c is '\n' or '\r')
            ? separator
            : " ";
    }

    /// <summary>
    /// Finds where content goes, and what whitespace should surround it.
    /// </summary>
    /// <remarks>
    /// The index counts content children only. A property element is where a member is written
    /// rather than a thing standing beside its siblings, and counting it would make "insert as the
    /// first control" land before a parent's resources — a position no caller can have meant. See
    /// <c>docs/adr/0008-an-index-counts-content.md</c>.
    /// </remarks>
    private (int Position, string Prefix, string Suffix) ContentInsertionPointFor(XamlElement parent, int index)
    {
        XamlElement[] children = [.. parent.ContentElements];

        if (children.Length == 0)
        {
            // After the members when there are any, because a parent that declares its resources
            // and then its content reads that way round, and the first control put into one
            // should not arrive above them.
            XamlElement? last = parent.MemberElements.LastOrDefault();

            return last is null
                ? FirstContentPointFor(parent)
                : (last.Span.End, LeadingWhitespaceOf(last), string.Empty);
        }

        if (index >= children.Length)
        {
            XamlElement last = children[^1];

            return (last.Span.End, LeadingWhitespaceOf(last), string.Empty);
        }

        XamlElement target = children[index];

        return (target.Span.Start, string.Empty, LeadingWhitespaceOf(target));
    }

    /// <summary>
    /// Finds where the first child goes in an element that holds nothing but whitespace and comments,
    /// and what whitespace should surround it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a line of its own, one step in from the element, wherever the element is laid out on lines —
    /// what it holds breaks a line, or its start tag begins one. It used to go straight after the start
    /// tag, on the start tag's line: a designer dropping a control into an emptied user control wrote
    /// <c>Height="420"&gt;&lt;Button /&gt;</c>, with the blank lines below it.
    /// </para>
    /// <para>
    /// Nothing already there moves or goes. The child arrives with a line break of its own in front of
    /// it, and the blank lines and comments the element held stay below it, as they would anywhere else;
    /// an element that held nothing gets its end tag back on a line at its own indentation. An element
    /// written inside another on one line stays on that line, and text the element holds is content of
    /// another kind, which leaves the child where it always went.
    /// </para>
    /// </remarks>
    private (int Position, string Prefix, string Suffix) FirstContentPointFor(XamlElement parent)
    {
        int start = parent.StartTagSpan.End;

        if (parent.EndTagSpan is not { } endTag || !HoldsOnlyBlanks(parent))
        {
            return (start, string.Empty, string.Empty);
        }

        bool broken = _document.SourceText.GetText(TextSpan.FromBounds(start, endTag.Start)).Contains('\n');

        if (!broken && !LaysOutOnLines(parent))
        {
            return (start, string.Empty, string.Empty);
        }

        string newLine = NewLineFor(parent);
        string indent = IndentOf(parent);
        string inner = indent + StepFor(parent);

        return broken
            ? (start, newLine + inner, string.Empty)
            : (endTag.Start, newLine + inner, newLine + indent);
    }

    /// <summary>Reports whether an element holds nothing but whitespace, line breaks and comments.</summary>
    /// <remarks>
    /// The whitespace between tags is trivia, not text; character data counts as blank only when it is
    /// whitespace through and through.
    /// </remarks>
    private bool HoldsOnlyBlanks(XamlElement element) =>
        element.Content.All(node => node switch
        {
            XamlTrivia { Kind: XamlTriviaKind.Whitespace or XamlTriviaKind.NewLine } => true,
            XamlComment => true,
            XamlText text => _document.SourceText.GetText(text.Span).All(char.IsWhiteSpace),
            _ => false,
        });

    /// <summary>
    /// Gets the whitespace immediately before a node, which is the indentation a sibling
    /// inserted next to it should match.
    /// </summary>
    private string LeadingWhitespaceOf(XamlSyntaxNode node)
    {
        int start = node.Span.Start;

        while (start > 0 && char.IsWhiteSpace(_document.SourceText[start - 1]))
        {
            start--;
        }

        return _document.SourceText.GetText(TextSpan.FromBounds(start, node.Span.Start));
    }

    /// <summary>
    /// Gets the indentation a node sits at, when it starts a line of its own.
    /// </summary>
    /// <remarks>
    /// Spaces and tabs only, and only when nothing else precedes it on its line. A node written
    /// after something else has no indentation of its own to speak of, and pretending otherwise
    /// would indent by whatever happened to come before it. Read from the node's own document,
    /// which is what lets a fragment ask it of the document it is lifted from.
    /// </remarks>
    internal static string IndentOf(XamlSyntaxNode node)
    {
        SourceText text = node.Document.SourceText;
        int start = node.Span.Start;

        while (start > 0 && text[start - 1] is ' ' or '\t')
        {
            start--;
        }

        return start == 0 || LineBreakEndsAt(text, start)
            ? text.GetText(TextSpan.FromBounds(start, node.Span.Start))
            : string.Empty;
    }

    /// <summary>Reports whether a node is the first thing on its line.</summary>
    private static bool StartsLine(XamlSyntaxNode node) =>
        StartsLineAt(node.Document.SourceText, node.Span.Start);

    /// <summary>Reports whether only spaces and tabs stand between a position and the start of its line.</summary>
    private static bool StartsLineAt(SourceText text, int position)
    {
        while (position > 0 && text[position - 1] is ' ' or '\t')
        {
            position--;
        }

        return position == 0 || LineBreakEndsAt(text, position);
    }

    /// <summary>
    /// Works out what one level of indentation is in this document, from what it already does.
    /// </summary>
    /// <remarks>
    /// The difference between an element's indentation and its parent's is the step the file was
    /// written with, whether that is two spaces, four, or a tab. Two spaces only when the
    /// document does not say — an element written inline, or a root with nothing above it.
    /// </remarks>
    private static string StepFor(XamlSyntaxNode node)
    {
        string indent = IndentOf(node);

        if (indent.Length > 0
            && node.Parent is XamlElement parent
            && IndentOf(parent) is { } outer
            && indent.Length > outer.Length
            && indent.StartsWith(outer, StringComparison.Ordinal))
        {
            return indent[outer.Length..];
        }

        return "  ";
    }

    /// <summary>
    /// Gets the line break the document is written with.
    /// </summary>
    /// <remarks>
    /// Looking backwards first, because the break above an element is the one it sits under.
    /// Looking forwards after that, because the root element has nothing above it — and answering
    /// "line feed" for it would put a line ending into a file written with carriage returns that
    /// the whole package otherwise promises to leave alone.
    /// </remarks>
    private string NewLineFor(XamlElement element)
    {
        SourceText text = _document.SourceText;

        for (int index = element.Span.Start; index > 0; index--)
        {
            if (text[index - 1] == '\n')
            {
                return index > 1 && text[index - 2] == '\r' ? "\r\n" : "\n";
            }
        }

        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
            }
        }

        return "\n";
    }

    /// <summary>
    /// Indents every line of an element but the first, which is already in place.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written against the line feed alone, so that a file using carriage returns keeps them: the
    /// insertion lands after the break either way, and normalising the endings here would rewrite
    /// every line of a block that was only supposed to move sideways.
    /// </para>
    /// <para>
    /// A line break inside a value is left alone. The text of a multi-line
    /// <c>&lt;TextBox&gt;</c> or a <c>CDATA</c> section is content the author wrote, not layout
    /// this may adjust, and indenting it would change what the control displays — which is
    /// exactly the kind of edit nobody asked for.
    /// </para>
    /// </remarks>
    internal static string Reindent(XamlElement element, string step) =>
        step.Length == 0 ? element.GetText() : Relayout(element, step, indent: true);

    /// <summary>Takes one level of indentation off every line of an element but the first.</summary>
    internal static string Outdent(XamlElement element, string step) =>
        step.Length == 0 ? element.GetText() : Relayout(element, step, indent: false);

    private static string Relayout(XamlElement element, string step, bool indent)
    {
        string text = element.GetText();
        List<TextSpan> values = ValueRunsOf(element);
        var builder = new StringBuilder(text.Length);

        for (int index = 0; index < text.Length; index++)
        {
            char character = text[index];

            builder.Append(character);

            if (character != '\n' || values.Any(run => run.Start <= index && index < run.End))
            {
                continue;
            }

            if (indent)
            {
                builder.Append(step);
            }
            else if (index + 1 + step.Length <= text.Length
                && text.AsSpan(index + 1, step.Length).SequenceEqual(step))
            {
                index += step.Length;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Finds the parts of an element's text that are a value rather than layout, relative to its
    /// own start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Whitespace between two elements is layout and is what indentation is made of; whitespace
    /// between two pieces of text is part of what that text says.
    /// </para>
    /// <para>
    /// So a value is taken to run from the first thing an element says to the last, and the line
    /// breaks in between belong to it. Reading the text nodes alone would miss them: a line break
    /// inside a value is trivia between two of them, not part of either.
    /// </para>
    /// </remarks>
    private static List<TextSpan> ValueRunsOf(XamlElement element)
    {
        int start = element.Span.Start;
        var runs = new List<TextSpan>();

        Collect(element);

        return runs;

        void Collect(XamlElement current)
        {
            XamlSyntaxNode[] said =
            [
                .. current.Content.Where(static node =>
                    node is XamlCData || (node is XamlText && node.GetSourceText().Trim().Length > 0)),
            ];

            if (said.Length > 0)
            {
                runs.Add(TextSpan.FromBounds(said[0].Span.Start - start, said[^1].Span.End - start));
            }

            foreach (XamlElement child in current.Content.OfType<XamlElement>())
            {
                Collect(child);
            }
        }
    }

    /// <summary>
    /// Works out what to remove along with an element: its own line when it has one to itself,
    /// otherwise just the element.
    /// </summary>
    private TextSpan SpanToRemoveFor(XamlElement element)
    {
        SourceText text = _document.SourceText;
        int start = element.Span.Start;

        while (start > 0 && text[start - 1] is ' ' or '\t')
        {
            start--;
        }

        bool ownsLineStart = start == 0 || LineBreakEndsAt(text, start);

        if (!ownsLineStart)
        {
            return element.Span;
        }

        int end = element.Span.End;

        while (end < text.Length && text[end] is ' ' or '\t')
        {
            end++;
        }

        // Only take the line break if nothing else follows the element on its line; otherwise
        // the next sibling would be pulled up onto the previous line.
        if (end < text.Length && text[end] == '\r')
        {
            end++;
        }

        if (end < text.Length && text[end] == '\n')
        {
            end++;
        }
        else if (end == element.Span.End)
        {
            return element.Span;
        }

        return TextSpan.FromBounds(start, end);
    }

    private static bool LineBreakEndsAt(SourceText text, int position) =>
        position > 0 && text[position - 1] is '\n' or '\r';

    /// <summary>
    /// Escapes only what the chosen quote character makes impossible to write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Values are raw attribute text: exactly what <see cref="XamlAttribute.GetValueText"/>
    /// returns, entity references included and unexpanded. Writing them back verbatim is what
    /// makes read-modify-write lossless — escaping <c>&amp;</c> here would turn a value read as
    /// <c>&amp;amp;</c> into <c>&amp;amp;amp;</c> on every save.
    /// </para>
    /// <para>
    /// The delimiting quote is the one exception, because a raw one would end the value early.
    /// Callers holding unescaped text should use <see cref="XamlLiteralValue.FromPlainText"/>.
    /// </para>
    /// </remarks>
    private static string Escape(string text, char quote) =>
        text.Replace(
            quote.ToString(),
            quote == '"' ? "&quot;" : "&apos;",
            StringComparison.Ordinal);
}
