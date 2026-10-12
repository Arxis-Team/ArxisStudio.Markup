using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// The two members Avalonia's XAML compiler emits into an <c>x:Class</c> type, found and checked
/// in one place.
/// </summary>
/// <remarks>
/// <para>
/// A static <c>!XamlIlPopulateOverride</c> field of type <see cref="Action{T}"/> and a
/// <c>!XamlIlPopulateTrampoline</c> method that consults it before running the compiled markup —
/// which is what the load call in a generated <c>InitializeComponent</c> is rewritten into. They
/// are generated members reached by name, and this library's rule is public Avalonia API only:
/// ADR 0014 records why <see cref="XamlLivePopulation"/> may stand on them, and ADR 0015 why a
/// session populating its own root may. Nothing else does, and everything that does comes here
/// for them, so a future Avalonia that stops emitting them changes one answer.
/// </para>
/// <para>
/// Both are checked for shape as well as name. A field of another type, whatever it is called,
/// is not the seam this rides on — installing into it would fail at invocation time, far from
/// the mistake.
/// </para>
/// <para>
/// It also keeps the one thing the two of them have to agree on: which types the current thread is
/// populating. A type constructed again while it is being populated places itself — directly, or
/// through a cycle of documents — and neither the root's loan nor a registration may then hand the
/// inner instance to the compiled markup: Avalonia's compiler builds a project whose markup places
/// itself, and that markup constructs the type again with the hook cleared, without end (ADR 0032).
/// </para>
/// </remarks>
internal sealed class XamlPopulateHook
{
    /// <summary>The types the current thread is populating.</summary>
    /// <remarks>
    /// Thread-static because population is re-entrant by design — a live document that places another
    /// registered control populates it in the middle of its own population — and the only re-entrancy
    /// that must be stopped is the same type appearing inside itself.
    /// </remarks>
    [ThreadStatic]
    private static HashSet<Type>? t_populating;

    /// <summary>The types among them found constructed again inside their own population.</summary>
    [ThreadStatic]
    private static HashSet<Type>? t_placingItself;

    /// <summary>The field Avalonia's XAML compiler emits for exactly this purpose.</summary>
    private const string OverrideFieldName = "!XamlIlPopulateOverride";

    /// <summary>The generated method that consults it and runs the compiled markup otherwise.</summary>
    private const string TrampolineMethodName = "!XamlIlPopulateTrampoline";

    private XamlPopulateHook(FieldInfo field, MethodInfo trampoline)
    {
        Field = field;
        Trampoline = trampoline;
    }

    /// <summary>Gets the override field, which is process-wide state on the type.</summary>
    internal FieldInfo Field { get; }

    /// <summary>Gets the method that runs the override, or the compiled markup when there is none.</summary>
    internal MethodInfo Trampoline { get; }

    /// <summary>Gets or sets what the type's instances are populated by instead of their compiled markup.</summary>
    internal Action<object>? Installed
    {
        get => Field.GetValue(null) as Action<object>;
        set => Field.SetValue(null, value);
    }

    /// <summary>
    /// Finds a type's hook, or nothing when the type has no compiled markup to stand in for.
    /// </summary>
    /// <param name="type">The type to look at.</param>
    /// <returns>The hook, or <see langword="null"/>.</returns>
    internal static XamlPopulateHook? Find(Type type)
    {
        FieldInfo? field = type.GetField(
            OverrideFieldName, BindingFlags.Static | BindingFlags.NonPublic);

        MethodInfo? trampoline = type.GetMethod(
            TrampolineMethodName, BindingFlags.Static | BindingFlags.NonPublic);

        if (field is null
            || trampoline is null
            || field.FieldType != typeof(Action<object>)
            || trampoline.GetParameters() is not [{ ParameterType: { } parameter }]
            || !parameter.IsAssignableFrom(type))
        {
            return null;
        }

        return new XamlPopulateHook(field, trampoline);
    }

    /// <summary>Marks a type as being populated on the current thread.</summary>
    /// <param name="type">The type about to be populated.</param>
    /// <returns>
    /// <see langword="false"/> when the type is already being populated further up this thread's
    /// stack — the instance asking is one the type places inside itself, and the type is marked as
    /// such for the population that owns it.
    /// </returns>
    internal static bool TryEnter(Type type)
    {
        if ((t_populating ??= []).Add(type))
        {
            return true;
        }

        (t_placingItself ??= []).Add(type);

        return false;
    }

    /// <summary>Ends a population <see cref="TryEnter"/> began.</summary>
    /// <param name="type">The type whose population has ended.</param>
    internal static void Leave(Type type)
    {
        t_populating?.Remove(type);
        t_placingItself?.Remove(type);
    }

    /// <summary>
    /// Reports whether a type's population on this thread has met an instance of the type inside it.
    /// </summary>
    /// <remarks>
    /// The compiled markup of such a type is not a fallback: it was compiled from a document that places
    /// the type too, as far as anything here can tell, and would construct it again without end.
    /// </remarks>
    /// <param name="type">The type being populated.</param>
    internal static bool PlacesItself(Type type) => t_placingItself?.Contains(type) == true;

    /// <summary>
    /// Runs the compiled populate over an instance, through the generated trampoline.
    /// </summary>
    /// <remarks>
    /// The override field is cleared for the length of the call, because the trampoline is the
    /// one caller that knows how to build the service provider the compiled markup expects, and
    /// it consults the field first. What goes back into the field afterwards is the caller's to
    /// decide — it knows whether it still owns it.
    /// </remarks>
    /// <param name="instance">The instance to populate.</param>
    internal void PopulateFromCompiledMarkup(object instance)
    {
        Field.SetValue(null, null);

        try
        {
            Trampoline.Invoke(null, [instance]);
        }
        catch (TargetInvocationException wrapped) when (wrapped.InnerException is not null)
        {
            // The compiled populate's own failure is the caller's to see, exactly as it would
            // have been with nothing installed.
            ExceptionDispatchInfo.Capture(wrapped.InnerException).Throw();
        }
    }
}
