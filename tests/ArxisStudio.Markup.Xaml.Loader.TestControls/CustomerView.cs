using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ArxisStudio.Markup.Xaml.Loader.TestControls;

/// <summary>
/// A class a document can name with <c>x:Class</c>, with handlers for it to attach.
/// </summary>
/// <remarks>
/// Its constructor deliberately does <em>not</em> call <c>InitializeComponent()</c>, which a
/// generated partial normally would. That is the double-initialisation hazard the default root
/// factory documents: a type that loads its own document in its constructor and is then
/// populated again ends up with everything twice. Leaving it out here keeps the tests about
/// <c>x:Class</c> rather than about that hazard.
/// </remarks>
public class CustomerView : UserControl
{
    /// <summary>Gets how many times <see cref="SaveClicked"/> has run.</summary>
    public int SaveClickCount { get; private set; }

    /// <summary>Gets how many times <see cref="CancelClicked"/> has run.</summary>
    public int CancelClickCount { get; private set; }

    /// <summary>Handles a click, named by a document's <c>Click</c> attribute.</summary>
    /// <param name="sender">The control that was clicked.</param>
    /// <param name="e">The event's arguments.</param>
    public void SaveClicked(object? sender, RoutedEventArgs e) => SaveClickCount++;

    /// <summary>Handles a click, named by a document's <c>Click</c> attribute.</summary>
    /// <param name="sender">The control that was clicked.</param>
    /// <param name="e">The event's arguments.</param>
    public void CancelClicked(object? sender, RoutedEventArgs e) => CancelClickCount++;
}

/// <summary>A class whose constructor throws, to prove failure is reported rather than thrown.</summary>
public class UnconstructableView : UserControl
{
    /// <summary>Always throws.</summary>
    /// <exception cref="InvalidOperationException">Always.</exception>
    public UnconstructableView() =>
        throw new InvalidOperationException("This view cannot be constructed, by design.");
}
