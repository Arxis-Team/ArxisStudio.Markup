using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ArxisStudio.Markup.Xaml;

/// <summary>One step of a <see cref="XamlElementPath"/>.</summary>
/// <param name="MemberName">
/// The member the step goes through — <c>Resources</c> for <c>&lt;Border.Resources&gt;</c> — or
/// <see langword="null"/> when it goes into the element's own content.
/// </param>
/// <param name="Index">The position among the children of that slot.</param>
public readonly record struct XamlPathStep(string? MemberName, int Index);

/// <summary>
/// Where an element sits, said in a way that survives the document being edited.
/// </summary>
/// <remarks>
/// <para>
/// An element belongs to the parse it came from: edit the document and every element in it is a
/// different object, at a different offset. A tool that has to remember something about an
/// element — that it is selected, that its node is expanded, that the inspector is showing it —
/// therefore cannot remember the element, and remembering the offset is worse, because an edit
/// above it moves the offset while the element stays where it was.
/// </para>
/// <para>
/// A path says the same thing structurally: third child, inside the member named
/// <c>Resources</c>, second child. That survives an edit anywhere else in the document, an undo
/// and a redo, and it means the same thing in two parses of the same text.
/// </para>
/// <para>
/// It is not a permanent identifier. Inserting a sibling above an element changes its path, which
/// is correct — it is a different position now. Where a document names its elements,
/// <see cref="XamlElement.Identity"/> is the stabler thing to key on.
/// </para>
/// </remarks>
public sealed class XamlElementPath : IEquatable<XamlElementPath>
{
    private XamlElementPath(ImmutableArray<XamlPathStep> steps) => Steps = steps;

    /// <summary>Gets the path to the root element, which is no steps at all.</summary>
    public static XamlElementPath Root { get; } = new([]);

    /// <summary>Gets the steps from the root, outermost first.</summary>
    public ImmutableArray<XamlPathStep> Steps { get; }

    /// <summary>Gets the path to what contains this one, or <see langword="null"/> for the root.</summary>
    /// <remarks>
    /// One step shorter, which for a step through a member is the element that declares it rather
    /// than the property element in between. This is what a tool falls back to when what it had
    /// selected has just been deleted: the position is gone, the thing it was in is still there.
    /// </remarks>
    public XamlElementPath? Parent =>
        Steps.IsEmpty ? null : new XamlElementPath(Steps[..^1]);

    /// <summary>Works out the path to an element.</summary>
    /// <param name="element">The element to describe.</param>
    /// <returns>The path from its document's root.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> is <see langword="null"/>.</exception>
    public static XamlElementPath Of(XamlElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var steps = new List<XamlPathStep>();
        XamlElement current = element;

        while (current.Parent is XamlElement parent)
        {
            if (parent.IsPropertyElementSyntax)
            {
                // A property element is not a step of its own: it is the name of the step that
                // goes through it, and its own parent is the element that has that member.
                if (parent.Parent is not XamlElement owner)
                {
                    break;
                }

                steps.Add(new XamlPathStep(parent.MemberName, IndexIn(parent, current)));
                current = owner;

                continue;
            }

            steps.Add(new XamlPathStep(null, IndexIn(parent, current)));
            current = parent;
        }

        steps.Reverse();

        return new XamlElementPath([.. steps]);
    }

    /// <summary>Finds the element a path leads to in a document.</summary>
    /// <param name="document">The document to walk.</param>
    /// <returns>The element, or <see langword="null"/> when the document no longer has one there.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is <see langword="null"/>.</exception>
    public XamlElement? Resolve(XamlDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        XamlElement? current = document.Root;

        foreach ((string? memberName, int index) in Steps)
        {
            if (current is null)
            {
                return null;
            }

            XamlElement? slot = memberName is null
                ? current
                : current.MemberElements.FirstOrDefault(member =>
                    string.Equals(member.MemberName, memberName, StringComparison.Ordinal));

            current = slot?.ContentElements.ElementAtOrDefault(index);
        }

        return current;
    }

    /// <inheritdoc />
    public bool Equals(XamlElementPath? other) => other is not null && Steps.SequenceEqual(other.Steps);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as XamlElementPath);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);

        foreach (XamlPathStep step in Steps)
        {
            hash.Add(step);
        }

        return hash.ToHashCode();
    }

    /// <summary>Renders the path the way it reads.</summary>
    /// <returns>Something like <c>/1/Resources:0</c>.</returns>
    public override string ToString() =>
        Steps.IsEmpty
            ? "/"
            : string.Concat(Steps.Select(static step =>
                step.MemberName is null ? $"/{step.Index}" : $"/{step.MemberName}:{step.Index}"));

    /// <summary>Finds a child's position among the content children of an element.</summary>
    private static int IndexIn(XamlElement parent, XamlElement child)
    {
        var index = 0;

        foreach (XamlElement candidate in parent.ContentElements)
        {
            if (ReferenceEquals(candidate, child))
            {
                return index;
            }

            index++;
        }

        return -1;
    }
}
