using DNF_ScriptViewer.Core.Diagnostics;
using DNF_ScriptViewer.Core.Model;

namespace DNF_ScriptViewer.Core.Runtime;

// 런타임 결과 — 구간 하나당 하나, 메모리에 둠. 모든 기록에 SourceRef(파일·줄) — 스크립트 탭 실행 줄 표시용
// 시간은 모두 구간 시작 기준 ms

/// <summary>상태(액션) 구간. End는 다음 상태가 시작하는 시각 (마지막은 구간 끝)</summary>
public sealed record StateSpan(string State, int Start, int End, int LoopCount, SourceRef Src);

/// <summary>상태 전이. BranchIndex = 그 시점에 평가한 브랜치 목록(파일 순서, Func 펼침) 안의 순번</summary>
public sealed record Transition(int Time, string From, string To, int BranchIndex, string Condition, SourceRef Src);

/// <summary>변수 변화 (값이 바뀐 때만)</summary>
public sealed record VarChange(int Time, string Name, RtValue Old, RtValue New, SourceRef Src);

/// <summary>타격 (범위 안 명중 가정). 피해 = 기본 % × 패시브 배율(곱연산)</summary>
public sealed record HitRecord(int Time, int SkillKey, int BuffKey, string BuffName,
    double BasePercent, double Multiplier, SourceRef Src)
{
    public double DamagePercent => BasePercent * Multiplier;
}

public enum BuffTarget { Player, Enemy }

/// <summary>버프·상태 이상 구간. End가 null이면 구간 끝까지</summary>
public sealed record BuffSpan(int BuffKey, string Name, BuffTarget Target, int Start, int? End, SourceRef Src);

public enum InputResult { Used, Discarded, Unused }

/// <summary>입력 눌림의 결과: 브랜치가 씀 / 상태가 바뀌어 버림 / 구간 끝까지 안 씀</summary>
public sealed record InputUse(int PressTime, string Command, InputResult Result, int? ResultTime, SourceRef Src, SourceRef? UsedBy);

/// <summary>조건은 참이었지만 타지 않은 브랜치 (미구현 ActionIgnore, 쿨타임, 대상 액션 없음). 같은 상태 안에서는 처음 한 번만 기록</summary>
public sealed record BranchSkip(int Time, string State, string Target, string Reason, SourceRef Src);

/// <summary>스킬 키 쿨타임 구간 (값·시작은 들어간 액션의 CoolTime)</summary>
public sealed record CoolTimeSpan(int SkillKey, string Action, int Start, int End, SourceRef Src);

/// <summary>원작 측정값(Mark)과 시뮬레이션에서 같은 상태로 들어간 시각의 비교. 순서대로 짝지음</summary>
public sealed record MarkCompare(MarkDef Mark, int? SimTime)
{
    public int? DiffMs => SimTime is int s && Mark.Time is int m ? s - m : null;
}

public sealed class SegmentResult
{
    public required string Segment { get; init; }
    public required int DurationMs { get; init; }
    public Dictionary<string, RtValue> InitialVars { get; } = new();
    public List<StateSpan> States { get; } = new();
    public List<Transition> Transitions { get; } = new();
    public List<VarChange> Vars { get; } = new();
    public List<HitRecord> Hits { get; } = new();
    public List<BuffSpan> Buffs { get; } = new();
    public List<InputUse> Inputs { get; } = new();
    public List<BranchSkip> Skips { get; } = new();
    public List<CoolTimeSpan> CoolTimes { get; } = new();
    public List<MarkCompare> Marks { get; } = new();
    public DiagnosticBag Warnings { get; } = new();

    /// <summary>시각 t의 상태 (재생 바 조회용)</summary>
    public StateSpan? StateAt(int t)
    {
        StateSpan? found = null;
        foreach (var s in States)
        {
            if (s.Start > t) break;
            found = s;
        }
        return found;
    }

    /// <summary>시각 t의 변수 값 (재생 시점 조건 재평가용)</summary>
    public Dictionary<string, RtValue> VarsAt(int t)
    {
        var vars = new Dictionary<string, RtValue>(InitialVars);
        foreach (var c in Vars)
        {
            if (c.Time > t) break;
            vars[c.Name] = c.New;
        }
        return vars;
    }

    public double TotalDamagePercent => Hits.Sum(h => h.DamagePercent);
}
