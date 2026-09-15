#nullable enable
using System.Threading;
using System.Threading.Tasks;

namespace StarMark.Abstractions;

/// <summary>规则动作接口。对应技术文档 §3.2。所有动作（打标签、移动文件、写笔记、标记重复）实现此接口。</summary>
public interface IRuleAction
{
    /// <summary>动作类型标识："add_tag" / "move_file" / "set_note" / "mark_duplicate"。</summary>
    string ActionType { get; }

    /// <summary>执行动作。规则引擎匹配到条件后调用。</summary>
    Task ExecuteAsync(Item item, ActionContext ctx);
}

/// <summary>动作执行上下文。</summary>
public sealed class ActionContext
{
    public required IServiceProvider Services { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>规则条件 DSL 根节点。</summary>
public sealed class RuleCondition
{
    /// <summary>"AND" / "OR"</summary>
    public string Logic { get; init; } = "AND";

    public required List<ConditionLeaf> Conditions { get; init; }
}

public sealed class ConditionLeaf
{
    /// <summary>字段名："type" / "stars_count" / "extra.topic" / "file_size" / "updated_at" / "title" ...</summary>
    public required string Field { get; init; }

    /// <summary>操作符："eq" / "ne" / "gt" / "lt" / "ge" / "le" / "contains" / "startswith" / "in".</summary>
    public required string Op { get; init; }

    /// <summary>比较值。类型随字段而定（数字、字符串、数组）。</summary>
    public object? Value { get; init; }
}

/// <summary>规则定义。对应 rules 表的一行。</summary>
public sealed class Rule
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public RuleCondition Condition { get; set; } = new() { Conditions = new() };
    public List<RuleActionDef> Actions { get; set; } = new();
    public int Priority { get; set; }
    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }
}

public sealed class RuleActionDef
{
    public string ActionType { get; init; } = string.Empty;
    public Dictionary<string, object?> Params { get; init; } = new();
}
