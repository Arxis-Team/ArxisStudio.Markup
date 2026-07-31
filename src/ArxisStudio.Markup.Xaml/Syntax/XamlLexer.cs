using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace ArxisStudio.Markup.Xaml;

/// <summary>
/// Turns a source snapshot into a token stream that accounts for every character in it.
/// </summary>
/// <remarks>
/// <para>
/// The stream is gap-free and contiguous by construction: the lexer never advances without
/// emitting, and never emits without advancing. Concatenating the text of every token
/// therefore reproduces the document exactly, which is what the round-trip guarantee rests on.
/// </para>
/// <para>
/// Nothing is normalised. Whitespace runs keep their exact characters, <c>\r\n</c> stays
/// <c>\r\n</c>, and entity references are recorded as references rather than expanded — the
/// document may be written back before anyone ever asks what <c>&amp;amp;</c> means.
/// </para>
/// <para>
/// Malformed input never throws. Anything the grammar cannot explain becomes a
/// <see cref="XamlTokenKind.Skipped"/> token plus a diagnostic, so the text survives to be
/// written back out.
/// </para>
/// </remarks>
internal sealed class XamlLexer
{
    private readonly SourceText _text;
    private readonly Uri? _documentUri;
    private readonly ImmutableArray<XamlToken>.Builder _tokens = ImmutableArray.CreateBuilder<XamlToken>();
    private readonly List<MarkupDiagnostic> _diagnostics = [];

    private int _position;

    private XamlLexer(SourceText text, Uri? documentUri)
    {
        _text = text;
        _documentUri = documentUri;
    }

    /// <summary>Lexes a snapshot.</summary>
    /// <param name="text">The snapshot to lex.</param>
    /// <param name="documentUri">The document's URI, attached to diagnostics.</param>
    /// <returns>The token stream and any diagnostics the lexer raised.</returns>
    public static (ImmutableArray<XamlToken> Tokens, ImmutableArray<MarkupDiagnostic> Diagnostics) Lex(
        SourceText text,
        Uri? documentUri)
    {
        var lexer = new XamlLexer(text, documentUri);

        lexer.Run();

        return (lexer._tokens.ToImmutable(), [.. lexer._diagnostics]);
    }

    private int Length => _text.Length;

    private char Current => _text[_position];

    private void Run()
    {
        while (_position < Length)
        {
            int start = _position;

            if (Current == '<')
            {
                LexMarkup();
            }
            else if (Current == '&')
            {
                LexReference();
            }
            else
            {
                LexContent();
            }

            // The one invariant that makes the stream lossless. A lexer that failed to advance
            // would loop forever on malformed input; one that advanced silently would drop
            // characters. Neither is recoverable, so fail loudly here rather than ship either.
            if (_position <= start)
            {
                throw new InvalidOperationException(
                    $"The lexer failed to advance at offset {start}. This is a bug in {nameof(XamlLexer)}.");
            }
        }

        _tokens.Add(new XamlToken(XamlTokenKind.EndOfFile, new TextSpan(Length, 0)));
    }

    /// <summary>Lexes everything that begins with <c>&lt;</c>.</summary>
    private void LexMarkup()
    {
        if (Matches("<!--"))
        {
            LexDelimited(XamlTokenKind.Comment, "-->", XamlDiagnosticCodes.UnterminatedComment, "comment");

            return;
        }

        if (Matches("<![CDATA["))
        {
            LexDelimited(XamlTokenKind.CData, "]]>", XamlDiagnosticCodes.UnterminatedCData, "CDATA section");

            return;
        }

        if (Matches("<!DOCTYPE"))
        {
            LexDelimited(XamlTokenKind.DocumentType, ">", XamlDiagnosticCodes.UnterminatedDocumentType, "DOCTYPE declaration");

            return;
        }

        if (Matches("<?"))
        {
            // "<?xml" is only the declaration when a space follows; "<?xml-stylesheet" is an
            // ordinary processing instruction.
            bool isDeclaration = Matches("<?xml")
                && (_position + 5 >= Length || IsWhitespaceOrNewLine(_text[_position + 5]));

            LexDelimited(
                isDeclaration ? XamlTokenKind.XmlDeclaration : XamlTokenKind.ProcessingInstruction,
                "?>",
                XamlDiagnosticCodes.UnterminatedProcessingInstruction,
                isDeclaration ? "XML declaration" : "processing instruction");

            return;
        }

        if (Matches("</"))
        {
            Emit(XamlTokenKind.LessThanSlash, 2);
            LexTag();

            return;
        }

        if (_position + 1 < Length && IsNameStart(_text[_position + 1]))
        {
            Emit(XamlTokenKind.LessThan, 1);
            LexTag();

            return;
        }

        // A bare '<' in content. It is kept rather than dropped so it can be written back.
        Report(
            XamlDiagnosticCodes.UnexpectedCharacter,
            "'<' does not begin a tag here. Write '&lt;' to include a literal '<' in content.",
            new TextSpan(_position, 1));

        Emit(XamlTokenKind.Skipped, 1);
    }

    /// <summary>Lexes the inside of a tag, up to and including whatever closes it.</summary>
    private void LexTag()
    {
        while (_position < Length)
        {
            char current = Current;

            if (current == '>')
            {
                Emit(XamlTokenKind.GreaterThan, 1);

                return;
            }

            if (current == '/' && _position + 1 < Length && _text[_position + 1] == '>')
            {
                Emit(XamlTokenKind.SlashGreaterThan, 2);

                return;
            }

            if (IsNewLine(current))
            {
                EmitNewLine();
            }
            else if (IsWhitespace(current))
            {
                EmitRun(XamlTokenKind.Whitespace, IsWhitespace);
            }
            else if (IsNameStart(current))
            {
                EmitRun(XamlTokenKind.Name, IsNameChar);
            }
            else if (current == ':')
            {
                Emit(XamlTokenKind.Colon, 1);
            }
            else if (current == '=')
            {
                Emit(XamlTokenKind.Equals, 1);
            }
            else if (current is '"' or '\'')
            {
                LexAttributeValue(current);
            }
            else if (current == '<')
            {
                // A new tag started before this one closed. Ending here stops one missing '>'
                // from swallowing the rest of the document.
                Report(
                    XamlDiagnosticCodes.UnterminatedTag,
                    "The tag is missing its closing '>'.",
                    new TextSpan(_position, 0));

                return;
            }
            else
            {
                Report(
                    XamlDiagnosticCodes.UnexpectedCharacter,
                    $"'{current}' is not valid inside a tag.",
                    new TextSpan(_position, 1));

                Emit(XamlTokenKind.Skipped, 1);
            }
        }

        Report(
            XamlDiagnosticCodes.UnterminatedTag,
            "The tag is missing its closing '>'.",
            new TextSpan(Length, 0));
    }

    /// <summary>Lexes a quoted attribute value, including both quotes.</summary>
    private void LexAttributeValue(char quote)
    {
        Emit(XamlTokenKind.Quote, 1);

        int runStart = _position;

        void FlushRun()
        {
            if (_position > runStart)
            {
                _tokens.Add(new XamlToken(
                    XamlTokenKind.AttributeValueText,
                    TextSpan.FromBounds(runStart, _position)));
            }
        }

        while (_position < Length)
        {
            char current = Current;

            if (current == quote)
            {
                FlushRun();
                Emit(XamlTokenKind.Quote, 1);

                return;
            }

            if (current == '<')
            {
                // '<' is forbidden in attribute values, so meeting one means the quote is
                // missing. Stopping bounds the damage to this attribute.
                FlushRun();
                Report(
                    XamlDiagnosticCodes.UnterminatedAttributeValue,
                    $"The attribute value is missing its closing {quote} character.",
                    new TextSpan(_position, 0));

                return;
            }

            if (current == '&')
            {
                FlushRun();
                LexReference();
                runStart = _position;

                continue;
            }

            _position++;
        }

        FlushRun();
        Report(
            XamlDiagnosticCodes.UnterminatedAttributeValue,
            $"The attribute value is missing its closing {quote} character.",
            new TextSpan(Length, 0));
    }

    /// <summary>
    /// Lexes an entity or character reference without expanding it.
    /// </summary>
    /// <remarks>
    /// Expanding here would be irreversible: <c>&amp;#65;</c> and <c>A</c> mean the same thing
    /// but are not the same source, and the document has to be writable back as it was.
    /// </remarks>
    private void LexReference()
    {
        int start = _position;
        int scan = _position + 1;

        if (scan < Length && _text[scan] == '#')
        {
            scan++;

            if (scan < Length && (_text[scan] == 'x' || _text[scan] == 'X'))
            {
                scan++;
                scan = ScanWhile(scan, static c => Uri.IsHexDigit(c));
            }
            else
            {
                scan = ScanWhile(scan, static c => c is >= '0' and <= '9');
            }
        }
        else
        {
            scan = ScanWhile(scan, IsNameChar);
        }

        if (scan > start + 1 && scan < Length && _text[scan] == ';')
        {
            _position = scan + 1;
            _tokens.Add(new XamlToken(XamlTokenKind.EntityReference, TextSpan.FromBounds(start, _position)));

            return;
        }

        Report(
            XamlDiagnosticCodes.InvalidEntityReference,
            "'&' does not begin a well-formed entity or character reference. Write '&amp;' for a literal '&'.",
            new TextSpan(start, 1));

        Emit(XamlTokenKind.Skipped, 1);
    }

    /// <summary>Lexes character data up to the next markup or reference.</summary>
    private void LexContent()
    {
        char current = Current;

        if (IsNewLine(current))
        {
            EmitNewLine();

            return;
        }

        if (IsWhitespace(current))
        {
            EmitRun(XamlTokenKind.Whitespace, IsWhitespace);

            return;
        }

        // Whitespace is split out so that later milestones can tell indentation from content
        // without re-scanning, and so a formatter has something to rewrite.
        int start = _position;

        while (_position < Length
            && Current is not ('<' or '&')
            && !IsWhitespaceOrNewLine(Current))
        {
            _position++;
        }

        _tokens.Add(new XamlToken(XamlTokenKind.Text, TextSpan.FromBounds(start, _position)));
    }

    /// <summary>Lexes a construct that runs to a fixed closing delimiter.</summary>
    private void LexDelimited(XamlTokenKind kind, string closing, string unterminatedCode, string description)
    {
        int start = _position;
        int end = IndexOf(closing, start);

        if (end < 0)
        {
            // Unterminated: take the rest of the document so the text is still accounted for.
            _position = Length;
            _tokens.Add(new XamlToken(kind, TextSpan.FromBounds(start, _position)));
            Report(
                unterminatedCode,
                $"The {description} is missing its closing '{closing}'.",
                TextSpan.FromBounds(start, Length));

            return;
        }

        _position = end + closing.Length;
        _tokens.Add(new XamlToken(kind, TextSpan.FromBounds(start, _position)));
    }

    private void Emit(XamlTokenKind kind, int length)
    {
        _tokens.Add(new XamlToken(kind, new TextSpan(_position, length)));
        _position += length;
    }

    private void EmitNewLine()
    {
        int length = Current == '\r' && _position + 1 < Length && _text[_position + 1] == '\n' ? 2 : 1;

        Emit(XamlTokenKind.NewLine, length);
    }

    private void EmitRun(XamlTokenKind kind, Func<char, bool> predicate)
    {
        int start = _position;

        _position = ScanWhile(_position, predicate);
        _tokens.Add(new XamlToken(kind, TextSpan.FromBounds(start, _position)));
    }

    private int ScanWhile(int from, Func<char, bool> predicate)
    {
        while (from < Length && predicate(_text[from]))
        {
            from++;
        }

        return from;
    }

    private bool Matches(string value)
    {
        if (_position + value.Length > Length)
        {
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (_text[_position + index] != value[index])
            {
                return false;
            }
        }

        return true;
    }

    private int IndexOf(string value, int from)
    {
        for (int start = from; start + value.Length <= Length; start++)
        {
            var matched = true;

            for (var index = 0; index < value.Length; index++)
            {
                if (_text[start + index] != value[index])
                {
                    matched = false;

                    break;
                }
            }

            if (matched)
            {
                return start;
            }
        }

        return -1;
    }

    private void Report(string code, string message, TextSpan span) =>
        _diagnostics.Add(MarkupDiagnostic.Parse(
            code, message, MarkupDiagnosticSeverity.Error, _documentUri, span));

    private static bool IsWhitespace(char value) => value is ' ' or '\t';

    private static bool IsNewLine(char value) => value is '\r' or '\n';

    private static bool IsWhitespaceOrNewLine(char value) => IsWhitespace(value) || IsNewLine(value);

    /// <summary>
    /// Determines whether a character may start an XML name.
    /// </summary>
    /// <remarks>
    /// The colon is excluded here as well as from <see cref="IsNameChar"/>. Admitting it would
    /// let a name run start on a character the run itself cannot consume, producing a
    /// zero-length token and a lexer that never advances.
    /// </remarks>
    private static bool IsNameStart(char value) =>
        char.IsLetter(value) || value == '_';

    /// <summary>
    /// Determines whether a character may continue an XML name.
    /// </summary>
    /// <remarks>
    /// The colon is excluded so that a prefix and a local name lex as separate tokens, which
    /// is what makes the prefix independently addressable. The dot is included: XAML's
    /// <c>Owner.Member</c> names are single names as far as XML is concerned.
    /// </remarks>
    private static bool IsNameChar(char value) =>
        char.IsLetterOrDigit(value) || value is '_' or '-' or '.';
}
