namespace DNF_ScriptViewer.Core.Model;

// 열거 값은 enum으로 정해서 사용. 데이터의 빈칸은 Unset, 모르는 값은 Unknown(원문은 Attrs에 남음)

public enum GuardType { Unset, Unknown, None, Invincible, SuperArmor }

/// <summary>작성만 하고 계산하지 않음 — 런타임은 항상 None으로 취급</summary>
public enum ActionSpeedType { Unset, Unknown, None, CastSpeed, AttackSpeed }

public enum AttackType { Unset, Unknown, AttackArrow, AttackRange, RepeatAttack }

public enum RangeType { Unset, Unknown, Round, Rect }

public enum BranchBlockKind { Normal, SuperCancel }

public enum BuffType { Unset, Unknown, Attack, Debuff, State, BuffEdit, StockRecharge }

public enum StackType { Unset, Unknown, None, Refresh, Stack }

public static class EnumParse
{
    /// <summary>빈칸 → Unset, 정의된 이름 → 그 값, 그 외 → Unknown</summary>
    public static T Parse<T>(string? raw) where T : struct, Enum
    {
        if (string.IsNullOrEmpty(raw)) return Enum.Parse<T>("Unset");
        if (raw is "Unset" or "Unknown") return Enum.Parse<T>("Unknown");
        return Enum.TryParse<T>(raw, ignoreCase: false, out var v) ? v : Enum.Parse<T>("Unknown");
    }
}
