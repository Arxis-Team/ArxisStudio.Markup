namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// What came of reading attribute text as a member's value.
/// </summary>
/// <remarks>
/// <para>
/// Text a member cannot hold is an ordinary user error — half a value, typed so far — so this is
/// a result rather than an exception, and it carries what to say about the failure rather than
/// leaving the caller to phrase it.
/// </para>
/// <para>
/// A tool validating a field as it is typed asks for this and shows <see cref="Error"/>; the same
/// conversion is what an update performs when the attribute is actually written, so what the
/// field says and what the document will do cannot drift apart.
/// </para>
/// </remarks>
public readonly record struct XamlValueConversionResult
{
    private XamlValueConversionResult(bool succeeded, object? value, string? error)
    {
        Succeeded = succeeded;
        Value = value;
        Error = error;
    }

    /// <summary>Gets whether the text became a value the member can hold.</summary>
    public bool Succeeded { get; }

    /// <summary>Gets the value, which is meaningful only when <see cref="Succeeded"/>.</summary>
    /// <remarks>
    /// <see langword="null"/> is a value like any other: a reference-typed member may be set to
    /// nothing, and the conversion says so by succeeding with no value.
    /// </remarks>
    public object? Value { get; }

    /// <summary>Gets what was wrong with the text, or <see langword="null"/> when nothing was.</summary>
    public string? Error { get; }

    /// <summary>Reports a conversion that produced a value.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The result.</returns>
    public static XamlValueConversionResult FromValue(object? value) => new(true, value, null);

    /// <summary>Reports text the member cannot hold.</summary>
    /// <param name="error">What was wrong with it, in a sentence a tool can show.</param>
    /// <returns>The result.</returns>
    public static XamlValueConversionResult FromError(string error) => new(false, null, error);
}
