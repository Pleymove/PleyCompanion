using System.Globalization;

namespace OpenClicky.Core.Launcher;

public static class Calculator
{
    public static string? Evaluate(string input)
    {
        if (input.Length is 0 or > 128) return null;
        var index = 0;
        void Spaces() { while (index < input.Length && char.IsWhiteSpace(input[index])) index++; }
        bool Take(char token)
        {
            Spaces();
            if (index == input.Length || input[index] != token) return false;
            index++;
            return true;
        }
        decimal Factor()
        {
            if (Take('-')) return -Factor();
            if (Take('+')) return Factor();
            if (Take('('))
            {
                var nested = Sum();
                if (!Take(')')) throw new FormatException();
                return nested;
            }
            var start = index;
            while (index < input.Length && (char.IsAsciiDigit(input[index]) || input[index] is ',' or '.')) index++;
            return decimal.Parse(input[start..index].Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }
        decimal Product()
        {
            var value = Factor();
            while (true)
            {
                if (Take('*')) value *= Factor();
                else if (Take('/')) value /= Factor();
                else return value;
            }
        }
        decimal Sum()
        {
            var value = Product();
            while (true)
            {
                if (Take('+')) value += Product();
                else if (Take('-')) value -= Product();
                else return value;
            }
        }
        try
        {
            var result = Sum();
            return index == input.Length ? result.ToString("G29", CultureInfo.CurrentCulture) : null;
        }
        catch (Exception error) when (error is FormatException or ArithmeticException) { return null; }
    }
}
