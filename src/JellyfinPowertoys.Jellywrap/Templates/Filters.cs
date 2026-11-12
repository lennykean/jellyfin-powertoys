using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using static JellyfinPowertoys.Jellywrap.Templates.BindingValue;

namespace JellyfinPowertoys.Jellywrap.Templates;

internal static class Filters
{
    private const int MinutesPerHour = 60;
    private const int HoursPerHalfDay = 12;
    private const double WholeHoursFrom = 10;

    private const uint HashMultiplier = 31;
    private const int MaxDecimals = 15;

    private static readonly CultureInfo _invariant = CultureInfo.InvariantCulture;

    private static readonly Dictionary<string, Filter> _all = new()
    {
        ["number"] = OfNumber(0, 1, (number, arguments) => Math.Round(number, Decimals(arguments)).ToString($"N{Decimals(arguments)}", _invariant)),
        ["round"] = OfNumber(0, 1, (number, arguments) => Math.Round(number, Decimals(arguments))),
        ["hours"] = OfNumber(0, 1, (number, arguments) => Hours(number, arguments.Count > 0 ? Print(arguments[0]) : null)),
        ["minutes"] = OfNumber(0, 0, (number, _) => Minutes(number)),
        ["hm"] = OfNumber(0, 0, (number, _) => HoursAndMinutes(number)),
        ["days"] = OfNumber(0, 0, (number, _) => Plural(Math.Round(number), "day", "days")),
        ["percent"] = OfNumber(0, 1, (number, arguments) => Percent(number, Decimals(arguments))),
        ["times"] = new(0, 0, (value, _) => Times(Number(value))),
        ["ordinal"] = OfNumber(0, 0, (number, _) => Ordinal((long)Math.Round(number))),
        ["plural"] = OfNumber(1, 2, (number, arguments) => Plural(number, Print(arguments[0]), PluralNoun(arguments))),
        ["noun"] = OfNumber(1, 2, (number, arguments) => number == 1 ? Print(arguments[0]) : PluralNoun(arguments)),
        ["mul"] = Arithmetic((left, right) => left * right),
        ["div"] = Arithmetic((left, right) => left / right),
        ["add"] = Arithmetic((left, right) => left + right),
        ["sub"] = Arithmetic((left, right) => left - right),
        ["mod"] = Arithmetic((left, right) => ((left % right) + right) % right),
        ["atLeast"] = Arithmetic(Math.Max),
        ["atMost"] = Arithmetic(Math.Min),
        ["floor"] = OfNumber(0, 0, (number, _) => Math.Floor(number)),
        ["ceil"] = OfNumber(0, 0, (number, _) => Math.Ceiling(number)),
        ["abs"] = OfNumber(0, 0, (number, _) => Math.Abs(number)),
        ["sqrt"] = OfNumber(0, 0, (number, _) => Math.Sqrt(number)),
        ["format"] = OfNumber(1, 1, (number, arguments) => Format(number, Print(arguments[0]))),
        ["sin"] = OfNumber(0, 0, (number, _) => Math.Sin(number)),
        ["cos"] = OfNumber(0, 0, (number, _) => Math.Cos(number)),
        ["upper"] = OfText(text => TextCase.Apply(text, "upper")),
        ["lower"] = OfText(text => TextCase.Apply(text, "lower")),
        ["title"] = OfText(text => TextCase.Apply(text, "title")),
        ["possessive"] = OfText(text => $"{text}'s"),
        ["words"] = new(0, 0, (value, _) => value is null ? 0.0 : (double)Print(value).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length),
        ["initials"] = OfText(text => Initials(text)),
        ["initial"] = new(0, 0, (value, _) => Initial(Print(value))),
        ["default"] = new(1, 1, (value, arguments) => Truthy(value) || value is double ? value : arguments[0]),
        ["then"] = new(1, 2, (value, arguments) => Truthy(value) ? arguments[0] : arguments.Count > 1 ? arguments[1] : string.Empty),
        ["date"] = new(1, 1, (value, arguments) => ParseDate(value) is { } date ? Format(date, Print(arguments[0])) : null),
        ["month"] = new(0, 0, (value, _) => value is double month ? MonthName((int)month) : ParseDate(value)?.ToString("MMMM", _invariant)),
        ["weekday"] = new(0, 0, (value, _) => value is double day ? WeekdayName((int)day) : ParseDate(value)?.ToString("dddd", _invariant)),
        ["hour"] = OfNumber(0, 0, (number, _) => ClockHour((int)number)),
        ["daysInMonth"] = new(0, 1, (value, arguments) => MonthOf(value, arguments) is { } month ? (double)DateTime.DaysInMonth(month.Year, month.Month) : null),
        ["firstWeekday"] = new(0, 1, (value, arguments) => MonthOf(value, arguments) is { } month ? (double)(int)month.DayOfWeek : null),
        ["hash"] = new(0, 1, (value, arguments) => value is null ? null : Hash(Print(value), (uint)WholeArgument(arguments))),
        ["rand"] = OfNumber(0, 0, (number, _) => PseudoRandom(number)),
        ["pick"] = OfNumber(1, null, (number, arguments) => arguments[(int)(((Math.Floor(number) % arguments.Count) + arguments.Count) % arguments.Count)]),
        ["sortBy"] = new(1, 2, (value, arguments) => SortBy(value, Print(arguments[0]), arguments.Count > 1 && Print(arguments[1]) == "asc")),
        ["where"] = new(2, 3, (value, arguments) => Where(value, Print(arguments[0]), arguments.Count > 2 ? Print(arguments[1]) : "==", arguments[^1])),
        ["take"] = new(1, 1, (value, arguments) => AsList(value)?.Take(WholeArgument(arguments)).ToList()),
        ["skip"] = new(1, 1, (value, arguments) => AsList(value)?.Skip(WholeArgument(arguments)).ToList()),
        ["first"] = new(0, 0, (value, _) => AsList(value)?.FirstOrDefault()),
        ["last"] = new(0, 0, (value, _) => AsList(value)?.LastOrDefault()),
        ["count"] = new(0, 0, (value, _) => (double)(AsList(value)?.Count ?? 0)),
        ["pluck"] = new(1, 1, (value, arguments) => AsList(value)?.Select(item => Get(item, Print(arguments[0]))).ToList()),
        ["join"] = new(1, 2, (value, arguments) => AsList(value) is { } list ? Join([.. list.Select(Print)], Print(arguments[0]), Print(arguments[^1])) : null),
        ["sum"] = new(0, 1, (value, arguments) => Numbers(value, arguments)?.Sum()),
        ["avg"] = new(0, 1, (value, arguments) => Numbers(value, arguments) is { Count: > 0 } numbers ? numbers.Average() : null),
        ["max"] = new(0, 1, (value, arguments) => Numbers(value, arguments) is { Count: > 0 } numbers ? numbers.Max() : null),
        ["min"] = new(0, 1, (value, arguments) => Numbers(value, arguments) is { Count: > 0 } numbers ? numbers.Min() : null),
        ["maxBy"] = new(1, 1, (value, arguments) => AsList(value)?.MaxBy(item => Number(Get(item, Print(arguments[0]))) ?? double.MinValue)),
        ["minBy"] = new(1, 1, (value, arguments) => AsList(value)?.MinBy(item => Number(Get(item, Print(arguments[0]))) ?? double.MaxValue)),
    };

    public static Filter? Find(string name)
    {
        return _all.GetValueOrDefault(name);
    }

    private static Filter OfNumber(int minArguments, int? maxArguments, Func<double, IReadOnlyList<object?>, object?> apply)
    {
        return new Filter(minArguments, maxArguments, (value, arguments) => Number(value) is { } number ? apply(number, arguments) : null);
    }

    private static Filter OfText(Func<string, object?> apply)
    {
        return new Filter(0, 0, (value, _) => value is null ? null : apply(Print(value)));
    }

    private static Filter Arithmetic(Func<double, double, double> operation)
    {
        return new Filter(1, 1, (value, arguments) => Number(value) is { } left && Number(arguments[0]) is { } right ? operation(left, right) : null);
    }

    private static int WholeArgument(IReadOnlyList<object?> arguments)
    {
        return arguments.Count > 0 && Number(arguments[0]) is { } number ? (int)number : 0;
    }

    private static int Decimals(IReadOnlyList<object?> arguments)
    {
        return Math.Clamp(WholeArgument(arguments), 0, MaxDecimals);
    }

    private static string? Format(IFormattable value, string format)
    {
        try
        {
            return value.ToString(format, _invariant);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string? MonthName(int month)
    {
        return month is >= 1 and <= 12 ? _invariant.DateTimeFormat.GetMonthName(month) : null;
    }

    private static string? WeekdayName(int day)
    {
        return Enum.IsDefined((DayOfWeek)day) ? _invariant.DateTimeFormat.GetDayName((DayOfWeek)day) : null;
    }

    private static List<object?>? AsList(object? value)
    {
        return value as List<object?>;
    }

    private static List<double>? Numbers(object? value, IReadOnlyList<object?> arguments)
    {
        return AsList(value)?.Select(item => Number(arguments.Count > 0 ? Get(item, Print(arguments[0])) : item)).OfType<double>().ToList();
    }

    private static List<object?>? Where(object? value, string path, string comparison, object? expected)
    {
        return AsList(value)?.Where(item => Matches(Get(item, path), comparison, expected)).ToList();
    }

    private static string Hours(double hours, string? style)
    {
        if (style == "number")
        {
            return HoursNumber(hours);
        }

        if (hours < 1)
        {
            return Minutes(hours);
        }

        var text = HoursNumber(hours);
        return style == "long" ? $"{text} {(text == "1.0" ? "hour" : "hours")}" : $"{text}{UnitMarks.Mark(" h")}";
    }

    private static string HoursNumber(double hours)
    {
        var tenths = Math.Round(hours * 10) / 10;
        return tenths < WholeHoursFrom ? tenths.ToString("0.0", _invariant) : Math.Round(hours).ToString("N0", _invariant);
    }

    private static string Minutes(double hours)
    {
        return $"{Math.Round(hours * MinutesPerHour).ToString("N0", _invariant)}{UnitMarks.Mark(" min")}";
    }

    private static string HoursAndMinutes(double hours)
    {
        var minutes = (long)Math.Round(hours * MinutesPerHour);
        return $"{minutes / MinutesPerHour}{UnitMarks.Mark(" h")} {minutes % MinutesPerHour}{UnitMarks.Mark(" m")}";
    }

    private static string Percent(double share, int decimals)
    {
        return $"{Math.Round(share * 100, decimals).ToString($"F{decimals}", _invariant)}{UnitMarks.Mark("%")}";
    }

    private static string? Times(double? count)
    {
        return count switch
        {
            1 => "once",
            2 => "twice",
            { } number => $"{Math.Round(number).ToString("N0", _invariant)} times",
            null => null,
        };
    }

    private static string Plural(double count, string one, string many)
    {
        return $"{count.ToString("#,0.##", _invariant)} {(count == 1 ? one : many)}";
    }

    private static string PluralNoun(IReadOnlyList<object?> arguments)
    {
        return arguments.Count > 1 ? Print(arguments[1]) : $"{Print(arguments[0])}s";
    }

    private static string Ordinal(long number)
    {
        var suffix = (number % 100) is 11 or 12 or 13 ? "th" : (number % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{number}{suffix}";
    }

    private static string ClockHour(int hour)
    {
        return $"{(hour % HoursPerHalfDay == 0 ? HoursPerHalfDay : hour % HoursPerHalfDay)} {(hour < HoursPerHalfDay ? "am" : "pm")}";
    }

    private static string Initials(string name)
    {
        var words = name.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length == 0 ? string.Empty : (FirstCharacter(words[0]) + (words.Length > 1 ? FirstCharacter(words[^1]) : string.Empty)).ToUpperInvariant();
    }

    private static string? Initial(string text)
    {
        return text.Trim() is { Length: > 0 } trimmed ? FirstCharacter(trimmed).ToUpperInvariant() : null;
    }

    private static string FirstCharacter(string text)
    {
        return text.EnumerateRunes().First().ToString();
    }

    private static string Join(IReadOnlyList<string> texts, string separator, string lastSeparator)
    {
        return texts.Count > 1 ? string.Join(separator, texts.Take(texts.Count - 1)) + lastSeparator + texts[^1] : texts.FirstOrDefault() ?? string.Empty;
    }

    private static DateTime? ParseDate(object? value)
    {
        return value is string text && DateTime.TryParse(text, _invariant, DateTimeStyles.RoundtripKind, out var date) ? date : null;
    }

    private static DateTime? MonthOf(object? value, IReadOnlyList<object?> arguments)
    {
        if (ParseDate(value) is { } date)
        {
            return new DateTime(date.Year, date.Month, 1);
        }

        var year = WholeArgument(arguments);
        var month = value is string name ? Array.IndexOf(_invariant.DateTimeFormat.MonthNames, name) + 1 : (int)(Number(value) ?? 0);
        return month is >= 1 and <= 12 && year >= 1 ? new DateTime(year, month, 1) : null;
    }

    private static double Hash(string text, uint seed)
    {
        var hash = seed;
        foreach (var rune in text.EnumerateRunes())
        {
            hash = unchecked((hash * HashMultiplier) + (uint)rune.Value);
        }

        return hash;
    }

    private static double PseudoRandom(double number)
    {
        var scrambled = Math.Sin((number * 127.1) + 311.7) * 43758.5453;
        return scrambled - Math.Floor(scrambled);
    }

    private static List<object?>? SortBy(object? value, string path, bool ascending)
    {
        var list = AsList(value);
        return list is null
            ? null
            : ascending
                ? [.. list.OrderBy(item => Get(item, path), Comparer<object?>.Create(Compare))]
                : [.. list.OrderByDescending(item => Get(item, path), Comparer<object?>.Create(Compare))];
    }
}
