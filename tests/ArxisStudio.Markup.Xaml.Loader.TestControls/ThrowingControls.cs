using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Metadata;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A control whose setters fail in the several ways real ones do.
/// </summary>
/// <remarks>
/// <para>
/// The point of these is that they are all legal. Nothing in the CLR or in Avalonia stops a setter
/// assigning its field, telling the world about it, and then deciding it is unhappy — and once one
/// has been called and thrown, no amount of looking at the object afterwards recovers what it did
/// on the way. A library that assumed otherwise would be right about
/// <see cref="ThrowsBeforeAssigning"/> and quietly wrong about the two below it.
/// </para>
/// <para>
/// Written for the tests rather than found among Avalonia's own controls, so that each way of
/// failing is present, unambiguous and stable.
/// </para>
/// </remarks>
public class ThrowingControl : ContentControl
{
    /// <summary>Refuses through Avalonia's own validation, which runs before the value is stored.</summary>
    /// <remarks>
    /// The well-behaved case, and still not one this library may call clean: validation is reached
    /// by calling the property system, and being sure it stopped there means knowing how this
    /// particular property is implemented.
    /// </remarks>
    public static readonly StyledProperty<int> ThrowsBeforeAssigningProperty =
        AvaloniaProperty.Register<ThrowingControl, int>(
            nameof(ThrowsBeforeAssigning), validate: static value => value >= 0);

    private string? _assigned;

    private string? _spreading;

    /// <summary>Gets or sets a value the control refuses to make negative.</summary>
    public int ThrowsBeforeAssigning
    {
        get => GetValue(ThrowsBeforeAssigningProperty);
        set => SetValue(ThrowsBeforeAssigningProperty, value);
    }

    /// <summary>Gets or sets a value that is stored and then complained about.</summary>
    /// <remarks>
    /// The case that makes the conservative rule necessary. Reading this property after the
    /// exception shows the new value; there is no framework anywhere that prevented it.
    /// </remarks>
    public string? AssignsThenThrows
    {
        get => _assigned;

        set
        {
            _assigned = value;

            throw new InvalidOperationException("Assigned, and then refused.");
        }
    }

    /// <summary>Gets or sets a value whose setter changes something else before it fails.</summary>
    /// <remarks>
    /// Worse than the one above, and just as legal: what changed is not the property that was
    /// written, so comparing that property before and after proves nothing at all.
    /// </remarks>
    public string? SpreadsThenThrows
    {
        get => _spreading;

        set
        {
            _spreading = value;

            Tag = $"touched by {value}";

            throw new InvalidOperationException("Changed something else, and then refused.");
        }
    }
}

/// <summary>
/// A collection that empties part of itself and then gives up.
/// </summary>
/// <remarks>
/// A collection is not a single assignment: rebuilding an element's content empties the original
/// before refilling it, and one that stops in the middle has lost items that nothing is going to
/// put back. It reports <see cref="System.Collections.IList.IsReadOnly"/> as
/// <see langword="false"/>, because a collection that says it is read-only is refused before
/// anything is called — which is the case this one exists not to be.
/// </remarks>
public sealed class BrittleControls : Collection<Control>
{
    /// <summary>Whether the next attempt to empty this collection should fail part-way.</summary>
    public bool Brittle { get; set; }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        if (!Brittle)
        {
            base.ClearItems();

            return;
        }

        if (Count > 0)
        {
            base.RemoveItem(0);
        }

        throw new InvalidOperationException("Emptied part of the way, and then refused.");
    }
}

/// <summary>A control whose content collection can be made to fail part-way through.</summary>
public class BrittleHost : Control
{
    /// <summary>Gets the controls the host arranges.</summary>
    [Content]
    public BrittleControls Panels { get; } = [];
}
