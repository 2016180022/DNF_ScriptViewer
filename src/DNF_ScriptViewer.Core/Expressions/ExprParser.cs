using System.Globalization;

namespace DNF_ScriptViewer.Core.Expressions;

/// <summary>
/// 식 파서. 우선순위(낮은 것부터): ?: → || → &amp;&amp; → == != → &lt; &lt;= &gt; &gt;= → + - → * / % → 단항 ! - → 숫자·이름·함수 호출·괄호
/// </summary>
public sealed class ExprParser
{
    private readonly string _s;
    private int _pos;

    private ExprParser(string s) => _s = s;

    public static bool TryParse(string text, out Expr? expr, out string error)
    {
        var p = new ExprParser(text);
        try
        {
            p.SkipWs();
            if (p.AtEnd) throw new FormatException("식이 비어 있음");
            expr = p.Conditional();
            p.SkipWs();
            if (!p.AtEnd) throw new FormatException($"{p._pos + 1}번째 글자 '{p._s[p._pos]}'에서 식이 끝나야 함");
            error = "";
            return true;
        }
        catch (FormatException ex)
        {
            expr = null;
            error = ex.Message;
            return false;
        }
    }

    private bool AtEnd => _pos >= _s.Length;

    private void SkipWs()
    {
        while (!AtEnd && char.IsWhiteSpace(_s[_pos])) _pos++;
    }

    /// <summary>연산자를 읽을 수 있으면 소비. 길이가 긴 것부터 넘겨야 함 ("&lt;=" 다음 "&lt;")</summary>
    private string? Accept(params string[] ops)
    {
        SkipWs();
        foreach (var op in ops)
        {
            if (string.CompareOrdinal(_s, _pos, op, 0, op.Length) != 0) continue;
            // "!" 와 "!=" 구분, "=" 단독은 허용하지 않음
            if (op == "!" && _pos + 1 < _s.Length && _s[_pos + 1] == '=') continue;
            _pos += op.Length;
            return op;
        }
        return null;
    }

    private void Expect(string op)
    {
        if (Accept(op) is null)
            throw new FormatException(AtEnd ? $"'{op}'가 필요한데 식이 끝남" : $"{_pos + 1}번째 글자에 '{op}'가 필요함");
    }

    private Expr Conditional()
    {
        var test = Or();
        if (Accept("?") is null) return test;
        var a = Conditional();
        Expect(":");
        var b = Conditional();
        return new ConditionalExpr(test, a, b);
    }

    private Expr Or() => LeftAssoc(And, "||");
    private Expr And() => LeftAssoc(Equality, "&&");
    private Expr Equality() => LeftAssoc(Relational, "==", "!=");
    private Expr Relational() => LeftAssoc(Additive, "<=", ">=", "<", ">");
    private Expr Additive() => LeftAssoc(Multiplicative, "+", "-");
    private Expr Multiplicative() => LeftAssoc(Unary, "*", "/", "%");

    private Expr LeftAssoc(Func<Expr> next, params string[] ops)
    {
        var left = next();
        while (Accept(ops) is { } op) left = new BinaryExpr(op, left, next());
        return left;
    }

    private Expr Unary()
    {
        if (Accept("!", "-") is { } op) return new UnaryExpr(op, Unary());
        return Primary();
    }

    private Expr Primary()
    {
        SkipWs();
        if (AtEnd) throw new FormatException("값이 필요한데 식이 끝남");
        var c = _s[_pos];

        if (c == '(')
        {
            _pos++;
            var inner = Conditional();
            Expect(")");
            return inner;
        }

        if (char.IsDigit(c) || (c == '.' && _pos + 1 < _s.Length && char.IsDigit(_s[_pos + 1])))
        {
            var start = _pos;
            while (!AtEnd && (char.IsDigit(_s[_pos]) || _s[_pos] == '.')) _pos++;
            var raw = _s[start.._pos];
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new FormatException($"숫자 '{raw}'를 읽을 수 없음");
            return new NumberExpr(v, raw);
        }

        if (char.IsLetter(c) || c == '_')
        {
            var start = _pos;
            while (!AtEnd && (char.IsLetterOrDigit(_s[_pos]) || _s[_pos] == '_')) _pos++;
            var name = _s[start.._pos];
            if (Accept("(") is null) return new IdentExpr(name);

            var args = new List<Expr>();
            if (Accept(")") is null)
            {
                do args.Add(Conditional());
                while (Accept(",") is not null);
                Expect(")");
            }
            return new CallExpr(name, args);
        }

        throw new FormatException($"{_pos + 1}번째 글자 '{c}'를 읽을 수 없음");
    }
}
