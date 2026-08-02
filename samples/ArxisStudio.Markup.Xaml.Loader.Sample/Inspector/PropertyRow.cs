using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArxisStudio.Markup.Xaml.Loader.Sample.Inspector;

/// <summary>
/// One property of the selected object, as the inspector shows it.
/// </summary>
/// <remarks>
/// A row carries what the property is and what it currently reads; how it is drawn and edited is
/// a data template in <c>ShowcaseApplication.axaml</c>. Committing a row does not set anything on
/// the object: it writes the attribute into the document, and the objects follow from the update.
/// </remarks>
internal abstract class PropertyRow(string name, string origin) : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the member's name.</summary>
    public string Name { get; } = name;

    /// <summary>Gets what to say about the row when it is pointed at.</summary>
    /// <remarks>
    /// The name is in the tooltip as well as in the label because an attached member is written
    /// <c>Owner.Member</c> and does not fit the column — <c>AutomationProperties.HelpText</c> is
    /// trimmed to something several other rows also trim to.
    /// </remarks>
    public string Hint => $"{Name} — {Origin}";

    private string _origin = origin;

    /// <summary>Gets or sets where the value shown came from.</summary>
    /// <remarks>
    /// It changes under the row: writing a property the document did not state makes it stated,
    /// and a row that went on calling itself inherited would be describing the document as it was
    /// one edit ago.
    /// </remarks>
    public string Origin
    {
        get => _origin;

        set
        {
            if (string.Equals(_origin, value, StringComparison.Ordinal))
            {
                return;
            }

            _origin = value;

            Raise();
            Raise(nameof(Hint));
        }
    }

    /// <summary>Raises <see cref="PropertyChanged"/>.</summary>
    /// <param name="property">The property that changed.</param>
    protected void Raise([CallerMemberName] string? property = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>A value that is written and read as text.</summary>
internal sealed class TextPropertyRow(string name, string origin, string text, Action<string> commit)
    : PropertyRow(name, origin)
{
    private string _text = text;

    /// <summary>Gets or sets the text the property reads.</summary>
    public string Text
    {
        get => _text;

        set
        {
            if (string.Equals(_text, value, StringComparison.Ordinal))
            {
                return;
            }

            _text = value;

            Raise();
            commit(value);
        }
    }
}

/// <summary>A value that is one of two.</summary>
internal sealed class TogglePropertyRow(string name, string origin, bool value, Action<string> commit)
    : PropertyRow(name, origin)
{
    private bool _value = value;

    /// <summary>Gets or sets whether the property is on.</summary>
    public bool IsChecked
    {
        get => _value;

        set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;

            Raise();
            commit(value ? "True" : "False");
        }
    }
}

/// <summary>A value that is one of a known set.</summary>
internal sealed class ChoicePropertyRow(
    string name,
    string origin,
    IReadOnlyList<string> options,
    string? selected,
    Action<string> commit)
    : PropertyRow(name, origin)
{
    private string? _selected = selected;

    /// <summary>Gets what the property may be set to.</summary>
    public IReadOnlyList<string> Options { get; } = options;

    /// <summary>Gets or sets which one it reads.</summary>
    public string? Selected
    {
        get => _selected;

        set
        {
            if (string.Equals(_selected, value, StringComparison.Ordinal))
            {
                return;
            }

            _selected = value;

            Raise();

            if (value is not null)
            {
                commit(value);
            }
        }
    }
}

/// <summary>
/// A value the document states as an expression rather than as a literal.
/// </summary>
/// <remarks>
/// Shown and not editable, on purpose. <c>Text="{Binding Customer.Name}"</c> must never be written
/// back as <c>Text="Alice"</c> because that is what it currently evaluates to, and an inspector
/// with a text box over it is the most natural way in the world for that to happen by accident.
/// </remarks>
internal sealed class ExpressionPropertyRow(string name, string origin, string text) : PropertyRow(name, origin)
{
    /// <summary>Gets the expression, as the document writes it.</summary>
    public string Text { get; } = text;
}
