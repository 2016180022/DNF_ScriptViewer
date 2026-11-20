namespace DNF_ScriptViewer.Core.Diagnostics;

/// <summary>진단 단계. 데이터 오류는 툴을 멈추지 않고 모두 진단으로만 남김</summary>
public enum Severity
{
    /// <summary>XML을 읽지 못하는 등 파일 단위 오류</summary>
    Error,
    /// <summary>규칙 위반·링크 끊김 등 고쳐야 하는 것</summary>
    Warning,
    /// <summary>측정 대기 — 아직 채우지 않은 빈 값 (경고와 따로 집계)</summary>
    Pending,
    /// <summary>참고 — 의도된 예외 등 (미구현 브랜치 집계 등)</summary>
    Info,
}

/// <summary>데이터 파일 안의 위치. 스크립트 탭의 실행 줄 표시에도 사용</summary>
public readonly record struct SourceRef(string File, int Line)
{
    public static readonly SourceRef None = new("", 0);
    public override string ToString() => Line > 0 ? $"{File}:{Line}" : File;
}

public sealed record Diagnostic(Severity Severity, string Code, string Message, SourceRef Src);

/// <summary>로더·검증기가 진단을 모으는 곳</summary>
public sealed class DiagnosticBag
{
    private readonly List<Diagnostic> _items = new();
    public IReadOnlyList<Diagnostic> Items => _items;

    public void Error(string code, SourceRef src, string message) => _items.Add(new(Severity.Error, code, message, src));
    public void Warn(string code, SourceRef src, string message) => _items.Add(new(Severity.Warning, code, message, src));
    public void Pending(string code, SourceRef src, string message) => _items.Add(new(Severity.Pending, code, message, src));
    public void Info(string code, SourceRef src, string message) => _items.Add(new(Severity.Info, code, message, src));
    public void Add(Diagnostic d) => _items.Add(d);
}
