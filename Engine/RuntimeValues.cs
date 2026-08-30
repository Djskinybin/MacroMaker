using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroMaker;

internal sealed class RuntimeValues
{
    private static readonly Regex BareIdentifierRegex = new(@"\b([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);
    private static readonly Regex ValidVariableNameRegex = new(@"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> BuiltInNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "MouseX", "MouseY", "ScreenWidth", "ScreenHeight", "Date", "Time", "Now"
    };
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();

    public RuntimeValues(MacroProject project)
    {
        Reset(project);
    }

    public void Reset(MacroProject project)
    {
        lock (_sync)
        {
            _values.Clear();
            foreach (var variable in project.Variables ?? new List<ProjectVariable>())
            {
                var name = NormalizeName(variable.Name);
                if (name.Length > 0)
                    _values[name] = variable.Value ?? string.Empty;
            }
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_sync)
            return new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);
    }

    public MacroCommand ResolveNumericExpressions(MacroCommand source)
    {
        if (source.ValueExpressions is null || source.ValueExpressions.Count == 0)
            return source;

        // Keep the original child lists so blocks still execute the real commands.
        // The clone is only used for this execution pass so variable-backed numbers
        // can be resolved again the next time the command runs.
        var resolved = source.ShallowCloneForExecution();

        foreach (var pair in source.ValueExpressions)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            var property = typeof(MacroCommand).GetProperty(pair.Key);
            if (property is null || !property.CanRead || !property.CanWrite || property.PropertyType != typeof(int))
                continue;

            var fallback = property.GetValue(source) is int value ? value : 0;
            property.SetValue(resolved, ResolveInt(pair.Value, fallback));
        }

        return resolved;
    }

    public string Get(string name)
    {
        var normalized = NormalizeName(name);
        if (TryGetBuiltIn(normalized, out var builtIn))
            return builtIn;
        lock (_sync)
        {
            if (_values.TryGetValue(normalized, out var value))
                return value;
        }

        throw new InvalidOperationException($"Variable '{normalized}' does not exist yet. Set it first or make sure the command that creates it runs before this one.");
    }

    public bool Exists(string name)
    {
        var normalized = NormalizeName(name);
        if (IsBuiltInName(normalized))
            return true;
        lock (_sync)
            return _values.ContainsKey(normalized);
    }

    public void Set(string name, string value)
    {
        var normalized = ValidateWritableVariableName(name);
        lock (_sync)
            _values[normalized] = value ?? string.Empty;
    }

    public void Add(string name, string amountOrText)
    {
        var normalized = ValidateWritableVariableName(name);
        var resolved = ResolveValue(amountOrText);
        lock (_sync)
        {
            if (!_values.TryGetValue(normalized, out var current))
                throw new InvalidOperationException($"Variable '{normalized}' does not exist yet. Use Set Variable first.");

            if (TryNumber(current, out var a) && TryNumber(resolved, out var b))
                _values[normalized] = FormatNumber(a + b);
            else
                _values[normalized] = current + resolved;
        }
    }

    public string ResolveValue(string? text)
    {
        var source = text ?? string.Empty;
        var resolved = ResolveText(source);
        if (!string.Equals(resolved, source, StringComparison.Ordinal))
            return resolved;

        // Treat valid arithmetic as a numeric assignment, while leaving ordinary
        // text (including text containing dashes or slashes) unchanged.
        if (Regex.IsMatch(source, @"[+\-*/()]"))
        {
            try
            {
                return ResolveInt(source, 0).ToString(CultureInfo.InvariantCulture);
            }
            catch
            {
                // Not a numeric formula; keep it as normal text.
            }
        }

        return source;
    }

    public string ResolveText(string? text)
    {
        var source = text ?? string.Empty;
        var trimmed = source.Trim();

        // Plain variable names are the only variable syntax. If the entire field
        // is a variable or built-in name, use its value. Otherwise keep the text literal.
        if (TryGetBuiltIn(trimmed, out var builtIn))
            return builtIn;
        lock (_sync)
        {
            if (_values.TryGetValue(trimmed, out var value))
                return value;
        }

        return source;
    }

    public int ResolveInt(string? expression, int fallback)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return fallback;

        var expanded = ResolveText(expression).Trim();

        // Numeric fields accept plain variable names and formulas. Examples:
        // FoundX, FoundX+20, MouseX.
        expanded = BareIdentifierRegex.Replace(expanded, match =>
        {
            var name = NormalizeName(match.Groups[1].Value);
            if (TryGetBuiltIn(name, out var builtIn))
                return builtIn;
            lock (_sync)
                return _values.TryGetValue(name, out var value) ? value : match.Value;
        });

        if (int.TryParse(expanded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
            return direct;

        if (double.TryParse(expanded, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return ToInt32(number, expression);

        var unknown = BareIdentifierRegex.Match(expanded);
        if (unknown.Success)
            throw new InvalidOperationException($"Variable '{unknown.Groups[1].Value}' does not exist yet. Set it first or make sure the command that creates it runs before this one.");

        try
        {
            return ToInt32(SimpleExpression.Evaluate(expanded), expression);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException($"Could not evaluate number or variable '{expression}'. Try a number, a variable like FoundX, or a formula like FoundX+20.");
        }
    }

    public bool Compare(string variableName, VariableCompareMode mode, string compareTo)
    {
        var left = Get(variableName);
        var right = ResolveValue(compareTo);

        if (TryNumber(left, out var leftNumber) && TryNumber(right, out var rightNumber))
        {
            return mode switch
            {
                VariableCompareMode.Equals => Math.Abs(leftNumber - rightNumber) < 0.0000001,
                VariableCompareMode.NotEquals => Math.Abs(leftNumber - rightNumber) >= 0.0000001,
                VariableCompareMode.GreaterThan => leftNumber > rightNumber,
                VariableCompareMode.GreaterThanOrEqual => leftNumber >= rightNumber,
                VariableCompareMode.LessThan => leftNumber < rightNumber,
                VariableCompareMode.LessThanOrEqual => leftNumber <= rightNumber,
                VariableCompareMode.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
                VariableCompareMode.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
                VariableCompareMode.EndsWith => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        return mode switch
        {
            VariableCompareMode.Equals => left.Equals(right, StringComparison.OrdinalIgnoreCase),
            VariableCompareMode.NotEquals => !left.Equals(right, StringComparison.OrdinalIgnoreCase),
            VariableCompareMode.GreaterThan => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) > 0,
            VariableCompareMode.GreaterThanOrEqual => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) >= 0,
            VariableCompareMode.LessThan => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) < 0,
            VariableCompareMode.LessThanOrEqual => string.Compare(left, right, StringComparison.OrdinalIgnoreCase) <= 0,
            VariableCompareMode.Contains => left.Contains(right, StringComparison.OrdinalIgnoreCase),
            VariableCompareMode.StartsWith => left.StartsWith(right, StringComparison.OrdinalIgnoreCase),
            VariableCompareMode.EndsWith => left.EndsWith(right, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    public static string NormalizeName(string? value) => (value ?? string.Empty).Trim();

    public static bool IsValidVariableName(string? value)
        => ValidVariableNameRegex.IsMatch(NormalizeName(value));

    public static bool IsBuiltInName(string? value)
        => BuiltInNames.Contains(NormalizeName(value));

    public static bool IsReservedVariableName(string? value)
        => IsBuiltInName(value);

    public static IReadOnlyCollection<string> BuiltInVariableNames => BuiltInNames;

    public static IEnumerable<string> ExtractIdentifiers(string? expression)
        => BareIdentifierRegex.Matches(expression ?? string.Empty).Select(match => match.Groups[1].Value);

    public static bool TryValidateNumericExpression(string? expression, ISet<string> knownVariables, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expression))
            return true;

        var expanded = BareIdentifierRegex.Replace(expression.Trim(), match =>
        {
            var name = NormalizeName(match.Groups[1].Value);
            return IsBuiltInName(name) || knownVariables.Contains(name) ? "1" : match.Value;
        });

        // Plain numeric literals (including scientific notation such as 1e3)
        // should not be mistaken for variable identifiers.
        if (int.TryParse(expanded, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            return true;
        if (double.TryParse(expanded, NumberStyles.Float, CultureInfo.InvariantCulture, out var literal))
        {
            if (double.IsFinite(literal) && literal >= int.MinValue && literal <= int.MaxValue)
                return true;
            error = "a result outside the supported number range";
            return false;
        }

        var unknown = BareIdentifierRegex.Match(expanded);
        if (unknown.Success)
        {
            error = $"unknown variable '{unknown.Groups[1].Value}'";
            return false;
        }

        try
        {
            var value = SimpleExpression.Evaluate(expanded);
            if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            {
                error = "a result outside the supported number range";
                return false;
            }
            return true;
        }
        catch
        {
            if (int.TryParse(expanded, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return true;
            if (double.TryParse(expanded, NumberStyles.Float, CultureInfo.InvariantCulture, out var direct))
            {
                if (double.IsFinite(direct) && direct >= int.MinValue && direct <= int.MaxValue)
                    return true;
                error = "a result outside the supported number range";
                return false;
            }
            error = "an invalid formula";
            return false;
        }
    }

    private static int ToInt32(double value, string? expression)
    {
        if (!double.IsFinite(value) || value < int.MinValue || value > int.MaxValue)
            throw new InvalidOperationException($"Number or formula '{expression}' is outside the supported whole-number range.");
        return (int)Math.Round(value);
    }

    private static string ValidateWritableVariableName(string? value)
    {
        var normalized = NormalizeName(value);
        if (!IsValidVariableName(normalized))
            throw new InvalidOperationException($"'{normalized}' is not a valid variable name. Use letters, numbers, and underscores, and do not start with a number.");
        if (IsReservedVariableName(normalized))
            throw new InvalidOperationException($"'{normalized}' is a built-in MacroMaker value and cannot be used as a saved variable name.");
        return normalized;
    }

    private static bool TryNumber(string value, out double number) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out number);

    private static string FormatNumber(double value)
    {
        if (Math.Abs(value - Math.Round(value)) < 0.0000001)
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    private static bool TryGetBuiltIn(string name, out string value)
    {
        switch (name.ToUpperInvariant())
        {
            case "MOUSEX":
            case "MOUSEY":
                if (NativeMethods.GetCursorPos(out var point))
                {
                    value = name.Equals("MOUSEX", StringComparison.OrdinalIgnoreCase)
                        ? point.X.ToString(CultureInfo.InvariantCulture)
                        : point.Y.ToString(CultureInfo.InvariantCulture);
                    return true;
                }
                break;
            case "SCREENWIDTH":
                value = NativeMethods.GetSystemMetrics(0).ToString(CultureInfo.InvariantCulture);
                return true;
            case "SCREENHEIGHT":
                value = NativeMethods.GetSystemMetrics(1).ToString(CultureInfo.InvariantCulture);
                return true;
            case "DATE":
                value = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            case "TIME":
                value = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                return true;
            case "NOW":
                value = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                return true;
        }

        value = string.Empty;
        return false;
    }
}

internal static class CoordinateResolver
{
    public static (int X, int Y) ResolvePoint(MacroCommand command, RuntimeValues values, bool end = false)
    {
        var rawX = values.ResolveInt(end ? command.EndXExpression : command.XExpression, end ? command.EndX : command.X);
        var rawY = values.ResolveInt(end ? command.EndYExpression : command.YExpression, end ? command.EndY : command.Y);

        return command.CoordinateMode switch
        {
            CoordinateMode.ActiveWindow when WindowTools.TryGetForegroundRect(out var region) => (region.X + rawX, region.Y + rawY),
            CoordinateMode.RelativeToMouse when NativeMethods.GetCursorPos(out var mouse) => (mouse.X + rawX, mouse.Y + rawY),
            _ => (rawX, rawY)
        };
    }

    public static MacroCommand ResolveImageSearch(MacroCommand source, RuntimeValues values)
    {
        var command = source.DeepClone();
        command.Id = source.Id;
        command.ImagePath = values.ResolveText(source.ImagePath);
        command.ImageFolder = values.ResolveText(source.ImageFolder);
        command.ImagePriority = source.ImagePriority.ToList();

        if (source.CoordinateMode == CoordinateMode.ActiveWindow && WindowTools.TryGetForegroundRect(out var window))
        {
            if (source.SearchWidth <= 0 || source.SearchHeight <= 0)
            {
                command.SearchX = window.X;
                command.SearchY = window.Y;
                command.SearchWidth = window.Width;
                command.SearchHeight = window.Height;
            }
            else
            {
                command.SearchX = window.X + source.SearchX;
                command.SearchY = window.Y + source.SearchY;
            }
        }
        else if (source.CoordinateMode == CoordinateMode.RelativeToMouse && NativeMethods.GetCursorPos(out var mouse))
        {
            if (source.SearchWidth > 0 && source.SearchHeight > 0)
            {
                command.SearchX = mouse.X + source.SearchX;
                command.SearchY = mouse.Y + source.SearchY;
            }
        }

        return command;
    }
}

internal sealed class SimpleExpression
{
    private readonly string _text;
    private int _index;

    private SimpleExpression(string text)
    {
        _text = text;
    }

    public static double Evaluate(string text)
    {
        var parser = new SimpleExpression(text);
        var value = parser.ParseExpression();
        parser.SkipSpaces();
        if (parser._index != parser._text.Length)
            throw new FormatException("Unexpected expression text.");
        return value;
    }

    private double ParseExpression()
    {
        var value = ParseTerm();
        while (true)
        {
            SkipSpaces();
            if (Take('+')) value += ParseTerm();
            else if (Take('-')) value -= ParseTerm();
            else return value;
        }
    }

    private double ParseTerm()
    {
        var value = ParseFactor();
        while (true)
        {
            SkipSpaces();
            if (Take('*')) value *= ParseFactor();
            else if (Take('/')) value /= ParseFactor();
            else return value;
        }
    }

    private double ParseFactor()
    {
        SkipSpaces();
        if (Take('+')) return ParseFactor();
        if (Take('-')) return -ParseFactor();
        if (Take('('))
        {
            var value = ParseExpression();
            SkipSpaces();
            if (!Take(')')) throw new FormatException("Missing ')'.");
            return value;
        }

        var start = _index;
        while (_index < _text.Length && (char.IsDigit(_text[_index]) || _text[_index] is '.' or ','))
            _index++;
        if (start == _index)
            throw new FormatException("Expected a number.");

        var token = _text[start.._index].Replace(',', '.');
        return double.Parse(token, CultureInfo.InvariantCulture);
    }

    private bool Take(char c)
    {
        if (_index < _text.Length && _text[_index] == c)
        {
            _index++;
            return true;
        }
        return false;
    }

    private void SkipSpaces()
    {
        while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
            _index++;
    }
}
