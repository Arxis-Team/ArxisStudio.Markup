namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// How far one step of an update got before it stopped.
/// </summary>
/// <remarks>
/// <para>
/// Internal, and the reason the update path can tell a clean refusal from a half-made change.
/// Every step that reaches a live object answers with one of these, and the run that drives them
/// keeps the worst answer: a step that refused before touching anything is a refusal only while
/// no earlier step has written, because once one has, the document cannot advance and the objects
/// already have.
/// </para>
/// <para>
/// A <see langword="bool"/> was what this used to be, which is why every failure claimed the
/// objects were untouched.
/// </para>
/// </remarks>
internal enum XamlMutationOutcome
{
    /// <summary>The step did what it was asked.</summary>
    Applied,

    /// <summary>The step refused without writing to any live object.</summary>
    Refused,

    /// <summary>The step wrote to a live object and then failed, so what it left behind is unknown.</summary>
    Inconsistent,
}
