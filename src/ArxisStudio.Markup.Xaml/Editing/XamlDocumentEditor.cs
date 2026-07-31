using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

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
/// using them would corrupt this one.
/// </para>
/// </remarks>
public sealed class XamlDocumentEditor
{
    private readonly XamlDocument _document;
    private readonly List<TextChange> _changes = [];
    private readonly List<MarkupDiagnostic> _diagnostics = [];

    internal XamlDocumentEditor(XamlDocument document) => _document = document;

    /// <summary>Gets the document these edits are computed against.</summary>
    public XamlDocument Document => _document;

    /// <summary>Gets a value indicating whether any edit has been recorded.</summary>
    public bool HasChanges => _changes.Count > 0;

    /// <summary>Gets the diagnostics raised while recording edits.</summary>
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
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">
    /// The position among <paramref name="parent"/>'s child elements. A value at or beyond the
    /// end appends.
    /// </param>
    /// <param name="xaml">The XAML text to insert.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="xaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="parent"/> belongs to a different document, or is self-closing and so has
    /// nowhere to put content.
    /// </exception>
    public XamlDocumentEditor InsertElement(XamlElement parent, int index, string xaml)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(xaml);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Validate(parent);

        if (parent.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Element '{parent.Name}' is self-closing and has no content to insert into. " +
                "Give it a start and end tag first.");
        }

        (int position, string prefix, string suffix) = ContentInsertionPointFor(parent, index);

        return Insert(position, prefix + xaml + suffix);
    }

    /// <summary>Inserts a copy of an element as a child of another.</summary>
    /// <param name="parent">The element to insert into.</param>
    /// <param name="index">The position among the parent's child elements.</param>
    /// <param name="element">The element whose text is inserted.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> or <paramref name="element"/> is <see langword="null"/>.</exception>
    public XamlDocumentEditor InsertElement(XamlElement parent, int index, XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        return InsertElement(parent, index, element.GetText());
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
    /// <param name="index">The position among the new parent's child elements.</param>
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

    /// <summary>Gets the text changes these edits amount to, ordered and non-overlapping.</summary>
    /// <returns>The changes, ready to apply to the document's snapshot.</returns>
    /// <exception cref="InvalidOperationException">Two edits would change overlapping regions.</exception>
    public ImmutableArray<TextChange> GetTextChanges()
    {
        TextChange[] ordered = [.. _changes.OrderBy(static change => change.Span.Start)];

        for (var index = 1; index < ordered.Length; index++)
        {
            if (ordered[index].Span.Start < ordered[index - 1].Span.End)
            {
                throw new InvalidOperationException(
                    $"Two edits change overlapping regions of the document ({ordered[index - 1].Span} " +
                    $"and {ordered[index].Span}). Apply them in separate operations.");
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
        if (!ReferenceEquals(node.Document, _document))
        {
            throw new InvalidOperationException(
                $"{node} belongs to a different document. Its spans point into different text, " +
                "so editing with it would corrupt this one. Find the node again in this document.");
        }
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

    /// <summary>Finds where content goes, and what whitespace should surround it.</summary>
    private (int Position, string Prefix, string Suffix) ContentInsertionPointFor(XamlElement parent, int index)
    {
        XamlElement[] children = [.. parent.Elements];

        if (children.Length == 0)
        {
            return (parent.StartTagSpan.End, string.Empty, string.Empty);
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
    /// Gets the whitespace immediately before an element, which is the indentation a sibling
    /// inserted next to it should match.
    /// </summary>
    private string LeadingWhitespaceOf(XamlElement element)
    {
        int start = element.Span.Start;

        while (start > 0 && char.IsWhiteSpace(_document.SourceText[start - 1]))
        {
            start--;
        }

        return _document.SourceText.GetText(TextSpan.FromBounds(start, element.Span.Start));
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
