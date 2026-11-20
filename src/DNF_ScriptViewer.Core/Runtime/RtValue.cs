using System.Globalization;

namespace DNF_ScriptViewer.Core.Runtime;

public enum RtKind
{
    None,
    Number,
    Bool,
    /// <summary>값을 알 수 없음 — 검증기의 구간 시작값 대조에서만 사용 (런타임에서는 나오지 않음)</summary>
    Unknown,
}

/// <summary>스크립트 식의 값: 숫자 / 참거짓 / None</summary>
public readonly record struct RtValue(RtKind Kind, double Num, bool Bool)
{
    public static readonly RtValue None = new(RtKind.None, 0, false);
    public static readonly RtValue Unknown = new(RtKind.Unknown, 0, false);
    public static readonly RtValue True = new(RtKind.Bool, 0, true);
    public static readonly RtValue False = new(RtKind.Bool, 0, false);
    public static RtValue Of(double n) => new(RtKind.Number, n, false);
    public static RtValue Of(bool b) => b ? True : False;

    public bool IsUnknown => Kind == RtKind.Unknown;

    /// <summary>조건으로 쓸 때: Bool은 그대로, 숫자는 0이 아니면 참, None은 거짓</summary>
    public bool IsTruthy => Kind switch
    {
        RtKind.Bool => Bool,
        RtKind.Number => Num != 0,
        _ => false,
    };

    /// <summary>계산에 쓸 때: None은 0, 참거짓은 1/0</summary>
    public double AsNumber => Kind switch
    {
        RtKind.Number => Num,
        RtKind.Bool => Bool ? 1 : 0,
        _ => 0,
    };

    /// <summary>데이터의 값 원문: "None"·"" → None, "True"/"False", 숫자. 그 외는 null</summary>
    public static RtValue? ParseLiteral(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0 || s == "None") return None;
        if (s == "True") return True;
        if (s == "False") return False;
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? Of(d) : null;
    }

    public override string ToString() => Kind switch
    {
        RtKind.Number => Num.ToString("0.###", CultureInfo.InvariantCulture),
        RtKind.Bool => Bool ? "True" : "False",
        RtKind.Unknown => "?",
        _ => "None",
    };
}
