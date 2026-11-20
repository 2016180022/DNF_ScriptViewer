using DNF_ScriptViewer.Core.Diagnostics;

namespace DNF_ScriptViewer.Core.Model;

/// <summary>XML 속성 원문 (작성 순서 유지 — 뷰어 스크립트 탭 표시용)</summary>
public sealed class AttrList
{
    private readonly List<KeyValuePair<string, string>> _items;
    public AttrList(List<KeyValuePair<string, string>> items) => _items = items;
    public IReadOnlyList<KeyValuePair<string, string>> Items => _items;

    public string? this[string name]
    {
        get
        {
            foreach (var kv in _items) if (kv.Key == name) return kv.Value;
            return null;
        }
    }
    public bool Has(string name) => this[name] is not null;
}

// ===================== 액션 안의 줄 =====================

/// <summary>ActionScript의 한 줄(요소). 모든 줄은 원문 속성과 줄 번호를 가짐</summary>
public abstract record ScriptNode(string Element, AttrList Attrs, SourceRef Src);

/// <summary>&lt;Variable Name StartTime EndTime Value Condition&gt; — 변수 선언·변경</summary>
public sealed record VariableNode(string Element, AttrList Attrs, SourceRef Src,
    string Name, TimeValue StartTime, TimeValue EndTime, string Value, string? Condition)
    : ScriptNode(Element, Attrs, Src);

/// <summary>&lt;Move Speed Direction /&gt; 또는 &lt;Move StartTime Distance Direction /&gt;</summary>
public sealed record MoveNode(string Element, AttrList Attrs, SourceRef Src,
    TimeValue StartTime, string? Speed, string? Distance, string? Direction, string? Condition)
    : ScriptNode(Element, Attrs, Src);

/// <summary>&lt;AttackFrame&gt; — 안에 BuffFrame·중첩 AttackFrame·Variable이 올 수 있음</summary>
public sealed record AttackFrameNode(string Element, AttrList Attrs, SourceRef Src,
    AttackType AttackType, RangeType RangeType, TimeValue StartTime, TimeValue EndTime, string? Condition,
    IReadOnlyList<ScriptNode> Children)
    : ScriptNode(Element, Attrs, Src);

/// <summary>BuffIndex 한 항목: "0(200)" = 0번을 200ms 뒤에</summary>
public readonly record struct BuffIndexEntry(int Index, int DelayMs);

/// <summary>&lt;BuffFrame SkillKey BuffIndex StartTime Condition /&gt; — 데이터 링크 시작점</summary>
public sealed record BuffFrameNode(string Element, AttrList Attrs, SourceRef Src,
    int? SkillKey, string BuffIndexRaw, IReadOnlyList<BuffIndexEntry> BuffIndex, bool BuffIndexValid,
    TimeValue StartTime, string? Condition)
    : ScriptNode(Element, Attrs, Src);

/// <summary>액션 안의 &lt;이름_Func /&gt; — Func 참조</summary>
public sealed record FuncRefNode(string Element, AttrList Attrs, SourceRef Src, string FuncName, bool HasChildren)
    : ScriptNode(Element, Attrs, Src);

/// <summary>&lt;Branch ActionName Condition ActionIgnore /&gt;</summary>
public sealed record BranchNode(string Element, AttrList Attrs, SourceRef Src,
    string ActionName, string Condition, bool ActionIgnore)
    : ScriptNode(Element, Attrs, Src);

/// <summary>&lt;NormalBranch&gt; / &lt;SuperCancelBranch&gt; — StartTime~EndTime 구간에서만 브랜치</summary>
public sealed record BranchBlockNode(string Element, AttrList Attrs, SourceRef Src,
    BranchBlockKind Kind, TimeValue StartTime, TimeValue EndTime, IReadOnlyList<ScriptNode> Items)
    : ScriptNode(Element, Attrs, Src);

/// <summary>알 수 없는 요소 (검증기가 경고)</summary>
public sealed record UnknownNode(string Element, AttrList Attrs, SourceRef Src)
    : ScriptNode(Element, Attrs, Src);

// ===================== 최상위 =====================

public sealed record CharacterVariable(string Name, string Value, SourceRef Src);

/// <summary>&lt;Constant Name Value Desc /&gt; — 이름은 대문자_밑줄 (11/19)</summary>
public sealed record Constant(string Name, string Value, string Desc, SourceRef Src);

/// <summary>최상위 &lt;이름_Func&gt; 정의</summary>
public sealed record FuncDef(string Name, IReadOnlyList<ScriptNode> Nodes, SourceRef Src);

/// <summary>액션(State) 하나</summary>
public sealed record ActionDef(string Name, AttrList Attrs, SourceRef Src,
    int? SkillKey, string CoolTime, string Animation,
    TimeValue StartTime, TimeValue EndTime,
    ActionSpeedType ActionSpeedType, GuardType GuardType,
    IReadOnlyList<ScriptNode> Nodes);

public sealed class ActionScriptData
{
    public string File { get; init; } = "";
    public List<CharacterVariable> Variables { get; } = new();
    public List<Constant> Constants { get; } = new();
    /// <summary>작성 순서 유지</summary>
    public List<FuncDef> FuncList { get; } = new();
    public List<ActionDef> ActionList { get; } = new();
    public Dictionary<string, FuncDef> Funcs { get; } = new();
    public Dictionary<string, ActionDef> Actions { get; } = new();

    public CharacterVariable? FindVariable(string name) => Variables.FirstOrDefault(v => v.Name == name);
    public Constant? FindConstant(string name) => Constants.FirstOrDefault(c => c.Name == name);
}
