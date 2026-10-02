using System;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// The half of the editor that writes an element's members as property elements.
/// </summary>
/// <remarks>
/// <para>
/// A member whose value is an object rather than text — <c>Design.DataContext</c>,
/// <c>Button.Flyout</c>, <c>Grid.ColumnDefinitions</c> — is written as an element named after it,
/// and changing one by hand is three edits a tool has to keep in step: find whether the element
/// already writes the member, replace what it says there without what the author wrote around it,
/// or write a new one where members are written, laid out the way the file is.
/// </para>
/// <para>
/// A member is found by the name a reader reads rather than by its spelling: the namespace its
/// prefix is bound to, and the dotted local name. Two names for one property — <c>Grid.Resources</c>
/// and <c>Panel.Resources</c> on a grid — are two names to this package, which has no types to tell
/// it otherwise, so a tool changing a member the document already writes passes the name the
/// document wrote it with.
/// </para>
/// </remarks>
public sealed partial class XamlDocumentEditor
{
    /// <summary>
    /// Sets a member of an element to markup, written as a property element: what an existing one
    /// says is replaced, and a missing one is written.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An existing member keeps its tags and what surrounds what it says. The replaced run goes from
    /// the first element, text or character data inside it to the last, so a comment above the
    /// value — Avalonia's templates write one inside <c>Design.DataContext</c> — stays where it is.
    /// </para>
    /// <para>
    /// A new member goes where members are written: in front of the element's content, or after the
    /// members it already has when it has no content. It is laid out as the siblings beside it are —
    /// on lines of its own, with what it contains one step further in, where they are on lines of
    /// their own — and a self-closing element is opened for it.
    /// </para>
    /// <para>
    /// The markup is written as given, apart from its line breaks, which become the document's, and
    /// the indentation of its lines after the first, which is put where it lands. A line break inside
    /// a value is left alone: it is part of what the value says.
    /// </para>
    /// <para>
    /// An element that writes the same member twice is not something any reader accepts. The first
    /// is the one changed, and the second is left for the loader to report.
    /// </para>
    /// </remarks>
    /// <param name="element">The element whose member is set.</param>
    /// <param name="name">
    /// The property element's name as it should be written, <c>Owner.Member</c> — from
    /// <see cref="Qualify"/> when the owner is in a namespace the document may not have declared.
    /// </param>
    /// <param name="contentXaml">What the member contains: one or more elements, or text.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="contentXaml"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is not written <c>Owner.Member</c>, or <paramref name="contentXaml"/>
    /// says nothing — a member is taken out with <see cref="RemovePropertyElement"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="element"/> belongs to a different document, or is itself a property element,
    /// which has no members of its own.
    /// </exception>
    public XamlDocumentEditor SetPropertyElement(XamlElement element, XamlQualifiedName name, string contentXaml)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentXaml);
        Validate(element);
        RequireMember(element, name);

        return PropertyElementOf(element, name) is { } existing
            ? SetMemberContent(existing, contentXaml)
            : InsertMember(element, name, contentXaml);
    }

    /// <summary>
    /// Takes out a member an element writes as a property element.
    /// </summary>
    /// <remarks>
    /// The property element goes with the line it sat on when it had that line to itself, as
    /// <see cref="RemoveElement"/> takes it. A member written as an attribute is
    /// <see cref="RemoveAttribute"/>'s to take, and an element that does not write the member is
    /// left as it is.
    /// </remarks>
    /// <param name="element">The element whose member is taken out.</param>
    /// <param name="name">The property element's name, <c>Owner.Member</c>.</param>
    /// <returns>This editor, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is not written <c>Owner.Member</c>.</exception>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="element"/> belongs to a different document, or is itself a property element.
    /// </exception>
    public XamlDocumentEditor RemovePropertyElement(XamlElement element, XamlQualifiedName name)
    {
        ArgumentNullException.ThrowIfNull(element);
        Validate(element);
        RequireMember(element, name);

        return PropertyElementOf(element, name) is { } existing ? RemoveElement(existing) : this;
    }

    /// <summary>Refuses a name that is not a member's, and an element that cannot have members.</summary>
    private static void RequireMember(XamlElement element, XamlQualifiedName name)
    {
        // A default name has no local name at all, and asking it for its parts would throw.
        if (name.LocalName is null
            || name.OwnerName is not { Length: > 0 }
            || name.MemberName is not { Length: > 0 })
        {
            throw new ArgumentException(
                $"'{name}' is not the name of a property element, which is written 'Owner.Member'.",
                nameof(name));
        }

        if (element.IsPropertyElementSyntax)
        {
            throw new InvalidOperationException(
                $"Element '{element.Name}' is a property element, which has no members of its own. " +
                "Set the member on the element that declares it.");
        }
    }

    /// <summary>Finds the property element that writes a member, by the name a reader reads.</summary>
    private static XamlElement? PropertyElementOf(XamlElement element, XamlQualifiedName name)
    {
        string? namespaceUri = element.NamespaceContext.LookupNamespace(name.Prefix);

        return element.MemberElements.FirstOrDefault(member =>
            string.Equals(member.Name.LocalName, name.LocalName, StringComparison.Ordinal)
            && (namespaceUri is null
                // A prefix bound to nothing names nothing to compare, so the spelling is all there is.
                ? member.NamespaceUri is null && string.Equals(member.Name.Prefix, name.Prefix, StringComparison.Ordinal)
                : string.Equals(member.NamespaceUri, namespaceUri, StringComparison.Ordinal)));
    }

    /// <summary>Replaces what an existing property element says, keeping everything around it.</summary>
    private XamlDocumentEditor SetMemberContent(XamlElement member, string contentXaml)
    {
        string newLine = NewLineFor(member);
        XamlSyntaxNode[] said = [.. member.Content.Where(static node => node is XamlElement or XamlText or XamlCData)];

        if (said.Length == 0)
        {
            string inner = IndentOf(member) + StepFor(member);

            return PutIntoSilent(member, Laid(contentXaml, inner, newLine), inner, newLine);
        }

        string indent = StartsLine(said[0]) ? IndentOf(said[0]) : IndentOf(member) + StepFor(member);

        return Replace(
            TextSpan.FromBounds(said[0].Span.Start, said[^1].Span.End),
            Laid(contentXaml, indent, newLine));
    }

    /// <summary>Writes a member an element does not have yet, where members are written.</summary>
    private XamlDocumentEditor InsertMember(XamlElement element, XamlQualifiedName name, string contentXaml)
    {
        string newLine = NewLineFor(element);

        // In front of the content, text included: a member written between two pieces of content
        // would split it, and content has to be written in one piece.
        if (element.Content.FirstOrDefault(IsContent) is { } first)
        {
            return Insert(
                first.Span.Start,
                MemberText(name, contentXaml, IndentOf(first), StepFor(first), StartsLine(first), newLine)
                    + LeadingWhitespaceOf(first));
        }

        if (element.MemberElements.LastOrDefault() is { } last)
        {
            return Insert(
                last.Span.End,
                LeadingWhitespaceOf(last)
                    + MemberText(name, contentXaml, IndentOf(last), StepFor(last), StartsLine(last), newLine));
        }

        string step = StepFor(element);
        string inner = IndentOf(element) + step;

        return PutIntoSilent(
            element,
            MemberText(name, contentXaml, inner, step, OnOwnLine(element), newLine),
            inner,
            newLine);
    }

    /// <summary>Reports whether a node is content — a thing the element contains — rather than a member or layout.</summary>
    private static bool IsContent(XamlSyntaxNode node) =>
        node is XamlElement { IsPropertyElementSyntax: false } or XamlText or XamlCData;

    /// <summary>Writes a property element at an indentation, on lines of its own or inline.</summary>
    private static string MemberText(
        XamlQualifiedName name,
        string contentXaml,
        string indent,
        string step,
        bool lines,
        string newLine)
    {
        string content = Laid(contentXaml, indent + step, newLine);

        return lines
            ? $"<{name}>{newLine}{indent}{step}{content}{newLine}{indent}</{name}>"
            : $"<{name}>{content}</{name}>";
    }

    /// <summary>
    /// Puts text into an element that says nothing yet: after any comment it holds, on a line of its
    /// own when the element closes on one, and opening the element when it is self-closing.
    /// </summary>
    private XamlDocumentEditor PutIntoSilent(XamlElement host, string text, string inner, string newLine)
    {
        bool lines = OnOwnLine(host);

        if (host.IsEmpty)
        {
            return OpenAndInsert(host, lines ? newLine + inner + text + newLine + IndentOf(host) : text);
        }

        XamlSyntaxNode? last = host.Content.LastOrDefault(static node => node is not XamlTrivia);

        return Insert(last?.Span.End ?? host.StartTagSpan.End, lines ? newLine + inner + text : text);
    }

    /// <summary>
    /// Reports whether what goes into an element that says nothing goes on a line of its own: a
    /// self-closing element that starts a line opens onto lines, and an open one already closes on a
    /// line of its own or does not.
    /// </summary>
    private static bool OnOwnLine(XamlElement host) =>
        host.IsEmpty
            ? StartsLine(host)
            : host.EndTagSpan is { } end && StartsLineAt(host.Document.SourceText, end.Start);

    /// <summary>
    /// Writes markup with the document's line breaks and its lines after the first indented to where
    /// it lands, leaving the line breaks inside a value as they are.
    /// </summary>
    /// <remarks>
    /// The markup is read inside a wrapper to tell its values from its layout, the same way an element
    /// of the document is read when it moves. A wrapper the markup itself breaks — markup that closes
    /// it early — gets the line breaks and nothing else.
    /// </remarks>
    private static string Laid(string xaml, string indent, string newLine)
    {
        string text = WithLineBreaks(xaml, newLine);

        if (indent.Length == 0 || !text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        string wrapped = $"<{FragmentName}>{text}</{FragmentName}>";

        if (XamlDocument.Parse(wrapped).Root is not { } wrapper || wrapper.Span.Length != wrapped.Length)
        {
            return text;
        }

        string laid = Reindent(wrapper, indent);

        return laid[(FragmentName.Length + 2)..^(FragmentName.Length + 3)];
    }
}
