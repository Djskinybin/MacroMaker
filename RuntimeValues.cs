using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroMaker;

internal sealed class RuntimeValues
{
    private static readonly Regex BareIdentifierRegex = new(@"\b([A-Za-z_][A-Za-z0-9_]*)\b", RegexOptions.Compiled);
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeValues(MacroProject project)
    {
        Reset(project);
    }

    public void Reset(MacroProject project)
    {
        _values.Clear();
        foreach (var variable in project.Variables ?? new List<ProjectVariable>())
        {
            var name = NormalizeName(variable.Name);
            if (name.Length > 0)
                _values[name] = variable.Value ?? string.Empty;
        }
    }

    public IReadOnlyDictionary<string, string> Snapshot() => new Dictionary<string, string>(_values, StringComparer.OrdinalIgnoreCase);

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
        return _values.TryGetValue(normalized, out var value) ? value : string.Empty;
    }

    public void Set(string name, string value)
    {
        var normalized = NormalizeName(name);
        if (normalized.Length == 0)
            throw new InvalidOperationException("Variable name cannot be empty.");
        _values[normalized] = value ?? string.Empty;
    }

    public void Add(string name, string amountOrText)
    {
        var current = Get(name);
        var resolved = ResolveText(amountOrText);
        if (TryNumber(current, out var a) && TryNumber(resolved, out var b))
            Set(name, FormatNumber(a + b));
        else
            Set(name, current + resolved);
    }

    public string ResolveText(string? text)
    {
        var source = text ?? string.Empty;
        var trimmed = source.Trim();

        // Plain variable names are the only variable syntax. If the entire field
        // is a variable or built-in name, use its value. Otherwise keep the text literal.
        if (TryGetBuiltIn(trimmed, out var builtIn))
            return builtIn;
        if (_values.TryGetValue(trimmed, out var value))
            return value;

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
            return _values.TryGetValue(name, out var value) ? value : match.Value;
        });

        if (int.TryParse(expanded, NumberStyles.Integer, CultureInfo.InvariantCulture, out var direct))
            return direct;

        if (double.TryParse(expanded, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return (int)Math.Round(number);

        try
        {
            return (int)Math.Round(SimpleExpression.Evaluate(expanded));
        }
        catch
        {
            throw new InvalidOperationException($"Could not evaluate number or variable '{expression}'. Try a number, a variable like FoundX, or a formula like FoundX+20.");
        }
    }

    public bool Compare(string variableName, VariableCompareMode mode, string compareTo)
    {
        var left = Get(variableName);
        var right = ResolveText(compareTo);

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
