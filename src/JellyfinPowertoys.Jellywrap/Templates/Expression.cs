using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal abstract class Expression
{
    public abstract object? Evaluate(Scope scope);
    public static Expression Parse(string text)
    {
        return new Parser(text).ParseAll();
    }

    private sealed class Literal(object? value) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            return value;
        }
    }

    private sealed class Path(string root, IReadOnlyList<object> steps) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            var value = scope.Get(root);
            JsonObject? parent = null;
            foreach (var step in steps)
            {
                switch (step, value)
                {
                    case (string name, JsonObject item):
                        parent = item;
                        value = BindingValue.FromNode(item[name]);
                        break;
                    case (string name, Func<string, object?> lookup):
                        value = lookup(name);
                        break;
                    case (int index, List<object?> list):
                        var fromStart = index < 0 ? list.Count + index : index;
                        value = fromStart >= 0 && fromStart < list.Count ? list[fromStart] : null;
                        break;
                    default:
                        return null;
                }
            }

            scope.Feature(parent);
            scope.Feature(value as JsonObject);
            return value;
        }
    }

    private sealed class Not(Expression inner) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            return !BindingValue.Truthy(inner.Evaluate(scope));
        }
    }

    private sealed class Logic(Expression left, Expression right, bool and) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            var leftHolds = BindingValue.Truthy(left.Evaluate(scope));
            return and ? leftHolds && BindingValue.Truthy(right.Evaluate(scope)) : leftHolds || BindingValue.Truthy(right.Evaluate(scope));
        }
    }

    private sealed class Comparison(Expression left, string comparison, Expression right) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            return BindingValue.Matches(left.Evaluate(scope), comparison, right.Evaluate(scope));
        }
    }

    private sealed class Piped(Expression input, Filter filter, IReadOnlyList<Expression> arguments) : Expression
    {
        public override object? Evaluate(Scope scope)
        {
            return filter.Apply(input.Evaluate(scope), [.. arguments.Select(argument => argument.Evaluate(scope))]);
        }
    }

    private sealed class Parser(string text)
    {
        private static readonly string[] _comparisons = ["==", "!=", "<=", ">=", "<", ">"];

        private int _position;

        public Expression ParseAll()
        {
            var expression = Or();
            SkipWhiteSpace();
            if (_position < text.Length)
            {
                throw Error($"unexpected \"{text[_position..]}\"");
            }

            return expression;
        }

        private Expression Or()
        {
            var expression = And();
            while (Take("||"))
            {
                expression = new Logic(expression, And(), and: false);
            }

            return expression;
        }

        private Expression And()
        {
            var expression = Unary();
            while (Take("&&"))
            {
                expression = new Logic(expression, Unary(), and: true);
            }

            return expression;
        }

        private Expression Unary()
        {
            return Peek("!=") || !Take("!") ? Compare() : new Not(Unary());
        }

        private Expression Compare()
        {
            var expression = Pipe();
            foreach (var comparison in _comparisons)
            {
                if (Take(comparison))
                {
                    return new Comparison(expression, comparison, Pipe());
                }
            }

            return expression;
        }

        private Expression Pipe()
        {
            var expression = Primary();
            while (!Peek("||") && Take("|"))
            {
                var name = Name() ?? throw Error("a filter name is missing after \"|\"");
                var filter = Filters.Find(name) ?? throw Error($"there is no filter \"{name}\"");
                var arguments = new List<Expression>();
                if (Take(":"))
                {
                    do
                    {
                        arguments.Add(Argument());
                    }
                    while (Take(","));
                }

                if (arguments.Count < filter.MinArguments || arguments.Count > filter.MaxArguments)
                {
                    var most = filter.MaxArguments is { } maxArguments ? $"{filter.MinArguments} to {maxArguments}" : $"{filter.MinArguments} or more";
                    throw Error($"filter \"{name}\" takes {most} arguments, not {arguments.Count}");
                }

                expression = new Piped(expression, filter, arguments);
            }

            return expression;
        }

        private Expression Argument()
        {
            return (Expression?)LiteralValue() ?? PathValue() ?? throw Error("a filter argument must be a number, a string, true, false or a path");
        }

        private Expression Primary()
        {
            if (Take("("))
            {
                var expression = Or();
                return Take(")") ? expression : throw Error("\")\" is missing");
            }

            return (Expression?)LiteralValue()
                ?? PathValue()
                ?? throw Error(_position < text.Length ? $"unexpected \"{text[_position..]}\"" : "an expression is missing");
        }

        private Literal? LiteralValue()
        {
            SkipWhiteSpace();
            if (_position < text.Length && text[_position] == '"')
            {
                var characters = new StringBuilder();
                for (_position++; _position < text.Length && text[_position] != '"'; _position++)
                {
                    if (text[_position] == '\\' && _position + 1 < text.Length)
                    {
                        _position++;
                    }

                    characters.Append(text[_position]);
                }

                if (_position++ >= text.Length)
                {
                    throw Error("a string is not closed");
                }

                return new Literal(characters.ToString());
            }

            var start = _position;
            if (_position < text.Length && text[_position] == '-')
            {
                _position++;
            }

            if (_position < text.Length && char.IsAsciiDigit(text[_position]))
            {
                while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.'))
                {
                    _position++;
                }

                return new Literal(double.Parse(text[start.._position], CultureInfo.InvariantCulture));
            }

            _position = start;
            var word = Name();
            switch (word)
            {
                case "true":
                    return new Literal(true);
                case "false":
                    return new Literal(false);
                case "null":
                    return new Literal(null);
                default:
                    _position = start;
                    return null;
            }
        }

        private Path? PathValue()
        {
            var root = Name();
            if (root is null)
            {
                return null;
            }

            var steps = new List<object>();
            while (true)
            {
                if (Take("."))
                {
                    steps.Add(Name(allowHyphens: true) ?? throw Error("a name is missing after \".\""));
                }
                else if (Take("["))
                {
                    var index = LiteralValue() is { } literal && literal.Evaluate(Scope.Empty) is double number && number == Math.Floor(number)
                        ? (int)number
                        : throw Error("an index must be a whole number");
                    steps.Add(index);
                    if (!Take("]"))
                    {
                        throw Error("\"]\" is missing");
                    }
                }
                else
                {
                    return new Path(root, steps);
                }
            }
        }
        private string? Name(bool allowHyphens = false)
        {
            SkipWhiteSpace();
            var start = _position;
            if (_position < text.Length && char.IsAsciiLetter(text[_position]))
            {
                while (_position < text.Length
                    && (char.IsAsciiLetterOrDigit(text[_position])
                        || (allowHyphens && text[_position] == '-' && _position + 1 < text.Length && char.IsAsciiLetterOrDigit(text[_position + 1]))))
                {
                    _position++;
                }
            }

            return _position > start ? text[start.._position] : null;
        }

        private bool Peek(string token)
        {
            SkipWhiteSpace();
            return string.CompareOrdinal(text, _position, token, 0, token.Length) == 0;
        }

        private bool Take(string token)
        {
            if (!Peek(token))
            {
                return false;
            }

            _position += token.Length;
            return true;
        }

        private void SkipWhiteSpace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private FormatException Error(string reason)
        {
            return new FormatException($"In \"{text}\": {reason}.");
        }
    }
}
