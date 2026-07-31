using System;

namespace ArxisStudio.Markup;

/// <summary>
/// Where a position in a projection's text came from.
/// </summary>
/// <remarks>
/// The document is always identified, because a projection splices text from several of them
/// and a bare offset is meaningless without knowing which one it counts characters in.
/// </remarks>
/// <param name="SourceUri">The document the position is in, when that document has a URI.</param>
/// <param name="Offset">The zero-based offset within that document's own text.</param>
/// <param name="IsOriginal">
/// Whether the position is in the text that was projected rather than in something spliced into
/// it.
/// </param>
public readonly record struct TextProjectionPosition(Uri? SourceUri, int Offset, bool IsOriginal);
