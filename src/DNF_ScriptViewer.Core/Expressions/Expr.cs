namespace DNF_ScriptViewer.Core.Expressions;

/// <summary>
/// 조건식·값 식의 구문 트리. 5번(검증기)에서는 문법·이름 검사에, 6번(런타임)에서는 평가에 사용
/// </summary>
public abstract record Expr;

public sealed record NumberExpr(double Value, string Raw) : Expr;

/// <summary>변수·상수·입력 커맨드·True/False/None</summary>
public sealed record IdentExpr(string Name) : Expr;

public sealed record CallExpr(string Name, IReadOnlyList<Expr> Args) : Expr;

/// <summary>! 또는 단항 -</summary>
public sealed record UnaryExpr(string Op, Expr Operand) : Expr;

public sealed record BinaryExpr(string Op, Expr Left, Expr Right) : Expr;

/// <summary>a ? b : c</summary>
public sealed record ConditionalExpr(Expr Test, Expr IfTrue, Expr IfFalse) : Expr;

public static class ExprWalker
{
    /// <summary>트리의 모든 노드를 앞에서부터 방문</summary>
    public static IEnumerable<Expr> All(Expr e)
    {
        yield return e;
        IEnumerable<Expr> children = e switch
        {
            CallExpr c => c.Args,
            UnaryExpr u => new[] { u.Operand },
            BinaryExpr b => new[] { b.Left, b.Right },
            ConditionalExpr t => new[] { t.Test, t.IfTrue, t.IfFalse },
            _ => Array.Empty<Expr>(),
        };
        foreach (var c in children)
            foreach (var x in All(c)) yield return x;
    }
}
