using DNF_ScriptViewer.Core.Expressions;

namespace DNF_ScriptViewer.Core.Runtime;

/// <summary>식 평가에 필요한 값(변수·상수·입력·함수)을 주는 쪽. 런타임과 검증기가 각자 구현</summary>
public interface IEvalHost
{
    /// <summary>변수·상수·입력 커맨드 이름의 값 (True/False/None은 평가기가 처리)</summary>
    RtValue Ident(string name);
    /// <summary>함수 호출. 인자는 필요할 때 eval로 평가</summary>
    RtValue Call(CallExpr call, Func<Expr, RtValue> eval);
}

/// <summary>
/// 식 평가기. Unknown을 3값 논리로 다룸 (거짓 &amp;&amp; 무엇이든 = 거짓, 참 || 무엇이든 = 참, 그 외 Unknown이 섞이면 Unknown).
/// 식 문자열은 한 번만 파싱해서 재사용
/// </summary>
public sealed class ExprEvaluator
{
    private readonly Dictionary<string, Expr?> _cache = new();

    /// <summary>파싱 실패 시 null (검증기 EXP001이 같은 식을 이미 경고)</summary>
    public Expr? Parse(string text)
    {
        if (_cache.TryGetValue(text, out var e)) return e;
        ExprParser.TryParse(text, out e, out _);
        _cache[text] = e;
        return e;
    }

    public RtValue Eval(string text, IEvalHost host) =>
        Parse(text) is { } e ? Eval(e, host) : RtValue.Unknown;

    public RtValue Eval(Expr e, IEvalHost host)
    {
        switch (e)
        {
            case NumberExpr n:
                return RtValue.Of(n.Value);

            case IdentExpr id:
                return id.Name switch
                {
                    "True" => RtValue.True,
                    "False" => RtValue.False,
                    "None" => RtValue.None,
                    _ => host.Ident(id.Name),
                };

            case CallExpr c:
                return host.Call(c, x => Eval(x, host));

            case UnaryExpr u:
            {
                var v = Eval(u.Operand, host);
                if (v.IsUnknown) return RtValue.Unknown;
                return u.Op == "!" ? RtValue.Of(!v.IsTruthy) : RtValue.Of(-v.AsNumber);
            }

            case ConditionalExpr t:
            {
                var test = Eval(t.Test, host);
                if (test.IsUnknown) return RtValue.Unknown;
                return Eval(test.IsTruthy ? t.IfTrue : t.IfFalse, host);
            }

            case BinaryExpr b:
                return Binary(b, host);
        }
        return RtValue.Unknown;
    }

    private RtValue Binary(BinaryExpr b, IEvalHost host)
    {
        if (b.Op is "&&" or "||")
        {
            var isAnd = b.Op == "&&";
            var l = Eval(b.Left, host);
            if (!l.IsUnknown && l.IsTruthy != isAnd) return RtValue.Of(!isAnd); // 거짓 && … / 참 || …
            var r = Eval(b.Right, host);
            if (!r.IsUnknown && r.IsTruthy != isAnd) return RtValue.Of(!isAnd);
            if (l.IsUnknown || r.IsUnknown) return RtValue.Unknown;
            return RtValue.Of(isAnd);
        }

        var a = Eval(b.Left, host);
        var c = Eval(b.Right, host);
        if (a.IsUnknown || c.IsUnknown) return RtValue.Unknown;

        switch (b.Op)
        {
            case "==": return RtValue.Of(Equal(a, c));
            case "!=": return RtValue.Of(!Equal(a, c));
            case "<": return RtValue.Of(a.AsNumber < c.AsNumber);
            case "<=": return RtValue.Of(a.AsNumber <= c.AsNumber);
            case ">": return RtValue.Of(a.AsNumber > c.AsNumber);
            case ">=": return RtValue.Of(a.AsNumber >= c.AsNumber);
            case "+": return RtValue.Of(a.AsNumber + c.AsNumber);
            case "-": return RtValue.Of(a.AsNumber - c.AsNumber);
            case "*": return RtValue.Of(a.AsNumber * c.AsNumber);
            case "/": return RtValue.Of(c.AsNumber == 0 ? 0 : a.AsNumber / c.AsNumber);
            case "%": return RtValue.Of(c.AsNumber == 0 ? 0 : a.AsNumber % c.AsNumber);
        }
        return RtValue.Unknown;
    }

    /// <summary>None은 None과만 같음. 그 외는 숫자로 비교 (참거짓은 1/0)</summary>
    private static bool Equal(RtValue a, RtValue b)
    {
        if (a.Kind == RtKind.None || b.Kind == RtKind.None) return a.Kind == b.Kind;
        return a.AsNumber == b.AsNumber;
    }

    /// <summary>식이 눌림으로 참조하는 입력 커맨드 이름 (브랜치를 탈 때 소비할 눌림. keyDown()의 인자는 제외)</summary>
    public IReadOnlyList<string> PressCommands(string text)
    {
        if (Parse(text) is not { } e) return Array.Empty<string>();
        // 레코드는 값으로 비교되므로, 같은 이름이라도 keyDown 인자 노드만 빼도록 참조로 비교
        var keyDownArgs = new HashSet<Expr>(ReferenceEqualityComparer.Instance);
        foreach (var c in ExprWalker.All(e).OfType<CallExpr>().Where(c => c.Name == "keyDown"))
            foreach (var a in c.Args) keyDownArgs.Add(a);
        return ExprWalker.All(e).OfType<IdentExpr>()
            .Where(id => !keyDownArgs.Contains(id) && KnownNames.IsInputCommand(id.Name))
            .Select(id => id.Name).Distinct().ToList();
    }
}
