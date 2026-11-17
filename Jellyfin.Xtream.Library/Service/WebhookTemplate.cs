// Copyright (C) 2024  Roland Breitschaft

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Xtream.Library.Service;

/// <summary>
/// Renders the small subset of Handlebars that webhook templates use: <c>{{Name}}</c>,
/// <c>{{{Name}}}</c>, <c>{{#if Name}}…{{else}}…{{/if}}</c> and <c>{{#unless Name}}…{{/unless}}</c>,
/// <c>{{#if_equals Name 'text'}}…{{else}}…{{/if_equals}}</c> and <c>{{#if_not_equals A B}}…{{/if_not_equals}}</c>.
/// <para>
/// <c>{{Name}}</c> is escaped for the body's format when it is JSON, so a title with a quote
/// cannot break the document; <c>{{{Name}}}</c> inserts the value as it is. A name that is
/// not known renders as nothing, as in Handlebars.
/// </para>
/// </summary>
public static class WebhookTemplate
{
    private const char LiteralMarker = '\u0001';

    // The default encoder writes a quote as ", which is valid but unreadable in a log or a chat message.
    private static readonly JsonSerializerOptions EscapeOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private enum TokenKind
    {
        Text,
        Variable,
        Raw,
        Comment,
        If,
        Unless,
        Else,
        EndIf,
        EndUnless,
        IfEquals,
        IfNotEquals,
        EndIfEquals,
        EndIfNotEquals,
    }

    private enum NodeKind
    {
        Text,
        Variable,
        If,
        Unless,
        IfEquals,
        IfNotEquals,
    }

    /// <summary>
    /// Renders a template.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="data">The values the template can use.</param>
    /// <param name="escapeJson">Whether <c>{{Name}}</c> escapes for a JSON string.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="FormatException">The template has a block that is never closed or closed wrongly.</exception>
    public static string Render(string template, IReadOnlyDictionary<string, object?> data, bool escapeJson)
        => Render(template, data, escapeJson ? WebhookEscape.Json : WebhookEscape.None);

    /// <summary>
    /// Renders a template.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <param name="data">The values the template can use.</param>
    /// <param name="escape">How <c>{{Name}}</c> is escaped.</param>
    /// <returns>The rendered text.</returns>
    /// <exception cref="FormatException">The template has a block that is never closed or closed wrongly.</exception>
    public static string Render(string template, IReadOnlyDictionary<string, object?> data, WebhookEscape escape)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var tokens = Tokenize(template);
        int pos = 0;
        var nodes = ParseNodes(tokens, ref pos, null);
        var sb = new StringBuilder();
        Emit(nodes, data, escape, sb);
        return sb.ToString();
    }

    /// <summary>
    /// Checks that a template can be rendered, without data.
    /// </summary>
    /// <param name="template">The template text.</param>
    /// <returns>Null when it is valid, otherwise what is wrong.</returns>
    public static string? Validate(string template)
    {
        try
        {
            Render(template, new Dictionary<string, object?>(), false);
            return null;
        }
        catch (FormatException ex)
        {
            return ex.Message;
        }
    }

    private static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        _ => true,
    };

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    private static string Escape(string value, WebhookEscape escape) => escape switch
    {
        WebhookEscape.Json => JsonEscape(value),
        WebhookEscape.Url => Uri.EscapeDataString(value),
        _ => value,
    };

    private static string JsonEscape(string value)
    {
        // Serialising a string yields its quoted, escaped form; drop the quotes.
        string quoted = JsonSerializer.Serialize(value, EscapeOptions);
        return quoted.Substring(1, quoted.Length - 2);
    }

    private static void Emit(List<Node> nodes, IReadOnlyDictionary<string, object?> data, WebhookEscape escape, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node.Kind)
            {
                case NodeKind.Text:
                    sb.Append(node.Text);
                    break;
                case NodeKind.Variable:
                    data.TryGetValue(node.Text, out var value);
                    string text = Format(value);
                    sb.Append(node.Raw ? text : Escape(text, escape));
                    break;
                case NodeKind.If:
                case NodeKind.Unless:
                    data.TryGetValue(node.Text, out var condition);
                    bool truthy = IsTruthy(condition);
                    if (node.Kind == NodeKind.Unless)
                    {
                        truthy = !truthy;
                    }

                    Emit(truthy ? node.Children : node.Else, data, escape, sb);
                    break;
                case NodeKind.IfEquals:
                case NodeKind.IfNotEquals:
                    bool same = Equal(node.Args[0], node.Args[1], data);
                    Emit((same == (node.Kind == NodeKind.IfEquals)) ? node.Children : node.Else, data, escape, sb);
                    break;
            }
        }
    }

    // An argument is a quoted literal ('x' or "x"), or the name of a value. Quoted ones keep a leading marker
    // so a literal that looks like a name is still read as text.
    private static List<string> SplitArgs(string text)
    {
        var args = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                i++;
                continue;
            }

            if (text[i] == '\x27' || text[i] == '"')
            {
                char quote = text[i];
                int close = text.IndexOf(quote, i + 1);
                if (close < 0)
                {
                    throw new FormatException($"A quote is never closed in '{text}'.");
                }

                args.Add(LiteralMarker + text.Substring(i + 1, close - i - 1));
                i = close + 1;
            }
            else
            {
                int start = i;
                while (i < text.Length && !char.IsWhiteSpace(text[i]))
                {
                    i++;
                }

                args.Add(text.Substring(start, i - start));
            }
        }

        if (args.Count != 2)
        {
            throw new FormatException($"A comparison needs exactly two values, found '{text}'.");
        }

        return args;
    }

    private static bool Equal(string a, string b, IReadOnlyDictionary<string, object?> data)
        => string.Equals(Resolve(a, data), Resolve(b, data), StringComparison.Ordinal);

    private static string Resolve(string arg, IReadOnlyDictionary<string, object?> data)
    {
        if (arg.Length > 0 && arg[0] == LiteralMarker)
        {
            return arg.Substring(1);
        }

        data.TryGetValue(arg, out var value);
        return Format(value);
    }

    private static List<Token> Tokenize(string template)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < template.Length)
        {
            int open = template.IndexOf("{{", i, StringComparison.Ordinal);
            if (open < 0)
            {
                tokens.Add(new Token(TokenKind.Text, template.Substring(i)));
                break;
            }

            if (open > i)
            {
                tokens.Add(new Token(TokenKind.Text, template.Substring(i, open - i)));
            }

            bool raw = template.AsSpan(open).StartsWith("{{{", StringComparison.Ordinal);
            string close = raw ? "}}}" : "}}";
            int start = open + (raw ? 3 : 2);
            int end = template.IndexOf(close, start, StringComparison.Ordinal);
            if (end < 0)
            {
                throw new FormatException("A '{{' in the template is never closed.");
            }

            string inner = template.Substring(start, end - start).Trim();
            tokens.Add(Classify(inner, raw));
            i = end + close.Length;
        }

        return tokens;
    }

    private static Token Classify(string inner, bool raw)
    {
        if (raw)
        {
            return new Token(TokenKind.Raw, inner);
        }

        if (inner.StartsWith('!'))
        {
            return new Token(TokenKind.Comment, string.Empty);
        }

        if (inner.StartsWith("#if ", StringComparison.Ordinal))
        {
            return new Token(TokenKind.If, inner.Substring(4).Trim());
        }

        if (inner.StartsWith("#unless ", StringComparison.Ordinal))
        {
            return new Token(TokenKind.Unless, inner.Substring(8).Trim());
        }

        if (inner.StartsWith("#if_equals ", StringComparison.Ordinal))
        {
            return new Token(TokenKind.IfEquals, inner.Substring(11).Trim());
        }

        if (inner.StartsWith("#if_not_equals ", StringComparison.Ordinal))
        {
            return new Token(TokenKind.IfNotEquals, inner.Substring(15).Trim());
        }

        return inner switch
        {
            "/if" => new Token(TokenKind.EndIf, string.Empty),
            "/unless" => new Token(TokenKind.EndUnless, string.Empty),
            "/if_equals" => new Token(TokenKind.EndIfEquals, string.Empty),
            "/if_not_equals" => new Token(TokenKind.EndIfNotEquals, string.Empty),
            "else" or "^" => new Token(TokenKind.Else, string.Empty),
            _ => new Token(TokenKind.Variable, inner),
        };
    }

    private static List<Node> ParseNodes(List<Token> tokens, ref int pos, TokenKind? until)
    {
        var nodes = new List<Node>();
        while (pos < tokens.Count)
        {
            var token = tokens[pos];
            switch (token.Kind)
            {
                case TokenKind.Text:
                    nodes.Add(new Node(NodeKind.Text) { Text = token.Value });
                    pos++;
                    break;
                case TokenKind.Variable:
                    nodes.Add(new Node(NodeKind.Variable) { Text = token.Value });
                    pos++;
                    break;
                case TokenKind.Raw:
                    nodes.Add(new Node(NodeKind.Variable) { Text = token.Value, Raw = true });
                    pos++;
                    break;
                case TokenKind.Comment:
                    pos++;
                    break;
                case TokenKind.IfEquals:
                case TokenKind.IfNotEquals:
                    pos++;
                    bool positive = token.Kind == TokenKind.IfEquals;
                    var compare = new Node(positive ? NodeKind.IfEquals : NodeKind.IfNotEquals) { Text = token.Value, Args = SplitArgs(token.Value) };
                    var compareEnd = positive ? TokenKind.EndIfEquals : TokenKind.EndIfNotEquals;
                    compare.Children = ParseNodes(tokens, ref pos, compareEnd);
                    if (pos < tokens.Count && tokens[pos].Kind == TokenKind.Else)
                    {
                        pos++;
                        compare.Else = ParseNodes(tokens, ref pos, compareEnd);
                    }

                    if (pos >= tokens.Count || tokens[pos].Kind != compareEnd)
                    {
                        throw new FormatException($"The block '{token.Value}' is never closed.");
                    }

                    pos++;
                    nodes.Add(compare);
                    break;
                case TokenKind.If:
                case TokenKind.Unless:
                    pos++;
                    var block = new Node(token.Kind == TokenKind.If ? NodeKind.If : NodeKind.Unless) { Text = token.Value };
                    var end = token.Kind == TokenKind.If ? TokenKind.EndIf : TokenKind.EndUnless;
                    block.Children = ParseNodes(tokens, ref pos, end);
                    if (pos < tokens.Count && tokens[pos].Kind == TokenKind.Else)
                    {
                        pos++;
                        block.Else = ParseNodes(tokens, ref pos, end);
                    }

                    if (pos >= tokens.Count || tokens[pos].Kind != end)
                    {
                        throw new FormatException($"The block '{token.Value}' is never closed.");
                    }

                    pos++;
                    nodes.Add(block);
                    break;
                case TokenKind.Else:
                case TokenKind.EndIf:
                case TokenKind.EndUnless:
                case TokenKind.EndIfEquals:
                case TokenKind.EndIfNotEquals:
                    if (until is null)
                    {
                        throw new FormatException("A block is closed that was never opened.");
                    }

                    // Handed back to the caller, which owns the matching open block.
                    return nodes;
            }
        }

        if (until is not null)
        {
            throw new FormatException("A block is never closed.");
        }

        return nodes;
    }

    private sealed record Token(TokenKind Kind, string Value);

    private sealed class Node
    {
        public Node(NodeKind kind) => Kind = kind;

        public NodeKind Kind { get; }

        public string Text { get; set; } = string.Empty;

        public bool Raw { get; set; }

        public List<Node> Children { get; set; } = new();

        public List<Node> Else { get; set; } = new();

        public List<string> Args { get; set; } = new();
    }
}
