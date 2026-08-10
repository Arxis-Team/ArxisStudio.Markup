using System;

namespace ArxisStudio.Markup.Xaml.Loader;

/// <summary>
/// Brackets every runtime compilation a session performs, for an environment whose assemblies do
/// not live where the process's compiler state does.
/// </summary>
/// <remarks>
/// <para>
/// This library compiles markup at runtime through Avalonia, and Avalonia's runtime compiler keeps
/// one set of emitted state for the whole process — a type system that remembers assemblies by
/// simple name from the moment it is first used, and a dynamic assembly whose generated code
/// resolves those names again when it runs. For an application loading its own markup that is
/// invisible. For a host that loads documents against assemblies in an isolated, replaceable load
/// context — a designer reloading a rebuilt project is the worked example — it is the one place
/// left where the first generation of an assembly outlives its replacement: the environment
/// resolves the new copy, and code generated against the compiler's cache binds the old one, and
/// the failure names a type that plainly exists.
/// </para>
/// <para>
/// The session cannot fix that, because it does not know the contexts exist; the environment's
/// owner does. So the environment may supply this scope, and the session enters it around every
/// operation that runs the compiler or the document's own code: creating a document's objects —
/// including the <c>x:Class</c> instance, whose constructor may compile markup of its own — and
/// rebuilding part of them for an update. An implementation puts the process-wide state wherever
/// the environment's assemblies live for exactly that long.
/// </para>
/// <para>
/// Entering must be cheap when there is nothing to do, because it happens on every load and every
/// update. The returned scope is disposed on the same thread that entered it, failure or not, and
/// entries may nest: an update can start while a constructor is still inside its own scope.
/// </para>
/// </remarks>
public interface IXamlCompilationScope
{
    /// <summary>Enters the scope.</summary>
    /// <returns>What to dispose when the compilation is done.</returns>
    IDisposable Enter();
}
