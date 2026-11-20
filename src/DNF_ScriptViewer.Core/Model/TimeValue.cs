using System.Globalization;

namespace DNF_ScriptViewer.Core.Model;

public enum TimeKind
{
    /// <summary>빈칸 — 측정 후 기입</summary>
    Empty,
    /// <summary>ms 숫자</summary>
    Ms,
    /// <summary>ActionEnd() — 액션의 EndTime</summary>
    ActionEnd,
    /// <summary>Escape — 액션을 나갈 때</summary>
    Escape,
    /// <summary>위에 해당하지 않는 값 (정수가 아닌 숫자 등). 검증기가 경고</summary>
    Invalid,
}

/// <summary>
/// 데이터의 시간 값 (StartTime·EndTime 등). 단위 ms, 런타임 틱 1ms
/// </summary>
public readonly record struct TimeValue(TimeKind Kind, int Ms, string Raw)
{
    public static readonly TimeValue Empty = new(TimeKind.Empty, 0, "");

    public static TimeValue Parse(string? raw)
    {
        var s = raw?.Trim() ?? "";
        if (s.Length == 0) return Empty;
        if (s == "ActionEnd()") return new(TimeKind.ActionEnd, 0, s);
        if (s == "Escape") return new(TimeKind.Escape, 0, s);
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) return new(TimeKind.Ms, ms, s);
        return new(TimeKind.Invalid, 0, s);
    }

    public override string ToString() => Raw;
}
