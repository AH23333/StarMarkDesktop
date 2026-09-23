#nullable enable
using System.Globalization;
using System.Text;

namespace StarMark.Core.Calc;

/// <summary>求值结果。<see cref="Ok"/> 为 false 时 <see cref="Error"/> 必非空——界面直接显示它，不再自行猜测原因。</summary>
public readonly record struct CalcResult(bool Ok, double Value, string? Error)
{
    public static CalcResult Fail(string error) => new(false, 0, error);

    /// <summary>结果行绑定用：成功给格式化数值，失败给原因。</summary>
    public string Display => Ok ? ExpressionEvaluator.Format(Value) : Error ?? "无法计算";
}

/// <summary>
/// 表达式求值器（计算器组件用）：递归下降、边解析边求值，零依赖、零动态编译、零语法树分配。
/// <para>
/// 优先级（低→高）：加/减 → 乘/除 → 一元正负 → 幂（右结合，指数可带符号）→ 百分号（后缀）→ 原子。
/// 所以 <c>-2^2 == -4</c>、<c>2^3^2 == 512</c>、<c>2^-1 == 0.5</c>。
/// </para>
/// <para>
/// 百分号按<em>计算器惯例</em>而非纯数学：<c>200+10% == 220</c>（取左值的 10%），而 <c>200*10% == 20</c>
/// （乘的是 0.1）。实现方式是让每个子表达式带一个"我是百分量"标志，只有直接作为加/减右操作数时被消费，
/// 其余场合一律清零——<c>200+10%*2</c> 因 <c>*</c> 优先级更高而等于 200.2，与用户所写的式子同构。
/// </para>
/// <para>
/// <b>契约：任何输入都不抛异常。</b>输入框是逐键求值的，一次抛出就是一弹窗或一崩溃。长度上限、
/// 嵌套深度上限（＝递归深度上限，挡 <c>((((…</c> 的栈溢出）、数值范围、非法字符全部转成错误串返回。
/// </para>
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>表达式长度上限（字符）。挡住把整段文本粘进输入框导致的无界解析。</summary>
    public const int MaxLength = 200;

    /// <summary>括号嵌套上限＝递归深度上限。64 层远超人手输入，又远低于默认栈的安全边界。</summary>
    public const int MaxDepth = 64;

    public static CalcResult Evaluate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return CalcResult.Fail("没有可计算的内容");
        if (text.Length > MaxLength) return CalcResult.Fail($"表达式过长（上限 {MaxLength} 个字符）");

        List<Token> tokens;
        try
        {
            var lexed = TryLex(text);
            if (!lexed.Ok) return CalcResult.Fail(lexed.Error!);
            tokens = lexed.Tokens!;
        }
        catch (Exception ex)
        {
            return CalcResult.Fail($"无法解析：{ex.GetType().Name}");
        }

        if (tokens.Count == 0) return CalcResult.Fail("没有可计算的内容");

        try
        {
            var p = new Parser(tokens, text.Length);
            var value = p.ParseAdditive();
            if (!p.Ok) return CalcResult.Fail(p.Error!);
            if (!p.AtEnd) return CalcResult.Fail($"多余的内容：第 {p.NextPosition} 个字符");
            // 先判"除以 0"再判 NaN/∞：n÷0 在 IEEE 里给 ±∞（0÷0 才给 NaN），
            // 顺序颠倒时 1/0 会被下面的范围分支报成"结果超出可表示范围"——用户按的是除号，不是溢出。
            if (p.DivisionByZero) return CalcResult.Fail("除数为 0");
            if (double.IsNaN(value)) return CalcResult.Fail("结果无定义");
            if (double.IsInfinity(value)) return CalcResult.Fail("结果超出可表示范围");
            return new CalcResult(true, value, null);
        }
        catch (Exception ex)
        {
            return CalcResult.Fail($"无法计算：{ex.GetType().Name}");
        }
    }

    // ───────────────────────── 分词 ─────────────────────────

    private enum Op { Plus, Minus, Multiply, Divide, Power, LParen, RParen }

    private enum TokKind { Number, Operator, Percent }

    private sealed record Token(TokKind Kind, double Number, Op Operator, int Position)
    {
        public bool Is(Op op) => Kind == TokKind.Operator && Operator == op;
    }

    private readonly record struct LexOutcome(bool Ok, string? Error, List<Token>? Tokens);

    private static LexOutcome TryLex(string s)
    {
        var list = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }

            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.' || s[i] == ',')) i++;
                // 科学计数法只在 e/E 后确实跟着指数时吃掉，否则把 e 留给常量 e（"2e" 由后续"多余的内容"报错）
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    var j = i + 1;
                    if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
                    if (j < s.Length && char.IsAsciiDigit(s[j]))
                    {
                        for (i = j; i < s.Length && char.IsAsciiDigit(s[i]); i++) { }
                    }
                }
                var raw = s.Substring(start, i - start);
                if (!IsNumericToken(raw))
                    return new LexOutcome(false, $"数字写法无法识别：「{raw}」", null);
                if (!double.TryParse(raw.Replace(",", string.Empty), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var num))
                    return new LexOutcome(false, $"数字超出范围：「{raw}」", null);
                list.Add(new Token(TokKind.Number, num, default, start + 1));
                continue;
            }

            if (char.IsLetter(c) || c == 'π')
            {
                var start = i;
                while (i < s.Length && (char.IsLetter(s[i]) || s[i] == 'π')) i++;
                var name = s.Substring(start, i - start);
                var value = name.ToLowerInvariant() switch
                {
                    "pi" or "π" => Math.PI,
                    "e" => Math.E,
                    _ => double.NaN,
                };
                if (double.IsNaN(value))
                    return new LexOutcome(false, $"不认识「{name}」（只支持 pi / π / e）", null);
                list.Add(new Token(TokKind.Number, value, default, start + 1));
                continue;
            }

            if (c == '%' || c == '％') { list.Add(new Token(TokKind.Percent, 0, default, i + 1)); i++; continue; }
            var mapped = MapOperator(c);
            if (mapped.HasValue) { list.Add(new Token(TokKind.Operator, 0, mapped.Value, i + 1)); i++; continue; }

            return new LexOutcome(false, $"不支持的字符「{c}」", null);
        }
        return new LexOutcome(true, null, list);
    }

    /// <summary>中文输入法开着时打出的全角/数学符号变体一律收进来——用户在 IME 状态下输入不是罕见事件。</summary>
    private static Op? MapOperator(char c) => c switch
    {
        '+' or '＋' => Op.Plus,
        '-' or '－' or '−' => Op.Minus,
        '*' or '×' or '✕' or '·' or '∙' => Op.Multiply,
        '/' or '÷' => Op.Divide,
        '^' => Op.Power,
        '(' or '（' or '[' or '【' or '{' => Op.LParen,
        ')' or '）' or ']' or '】' or '}' => Op.RParen,
        _ => null,
    };

    /// <summary>
    /// 数字串文法自检：<c>123</c>｜<c>1,234,567</c>｜<c>1.5</c>｜<c>.5</c>｜<c>1e3</c>｜<c>1.5E-3</c>。
    /// 逗号只允许"整数部分每三位一组"，小数点后不允许分组——这样 <c>1,23</c>、<c>,5</c>、<c>1.2.3</c>、
    /// <c>1.</c> 才会被判错，而不是静默按 123 / 5 / 12 / 1 算掉（静默改值比报错危险得多）。
    /// </summary>
    private static bool IsNumericToken(string raw)
    {
        var eIndex = raw.IndexOfAny(new[] { 'e', 'E' });
        var mantissa = eIndex < 0 ? raw : raw.Substring(0, eIndex);
        if (mantissa.Length == 0) return false;

        var i = 0;
        var intDigits = 0;
        while (i < mantissa.Length && char.IsAsciiDigit(mantissa[i])) { i++; intDigits++; }

        if (i < mantissa.Length && mantissa[i] == ',')
        {
            // 千分位：首段 1–3 位，其后每段恰好 3 位且只再跟逗号或结尾
            if (intDigits == 0 || intDigits > 3) return false;
            while (i < mantissa.Length && mantissa[i] == ',')
            {
                i++;
                var group = 0;
                while (group < 3 && i < mantissa.Length && char.IsAsciiDigit(mantissa[i])) { i++; group++; }
                if (group != 3) return false;
            }
        }
        else if (intDigits == 0 && (i >= mantissa.Length || mantissa[i] != '.'))
        {
            return false;   // 既无整数位也不是 ".5" 形式（含孤立的一个点）
        }

        if (i < mantissa.Length && mantissa[i] == '.')
        {
            i++;
            var frac = 0;
            while (i < mantissa.Length && char.IsAsciiDigit(mantissa[i])) { i++; frac++; }
            if (frac == 0) return false;   // "1." 与 "1.a" 都判错，不按 1 静默算掉
        }
        if (i != mantissa.Length) return false;
        return eIndex < 0 || IsExponent(raw.Substring(eIndex + 1));
    }

    private static bool IsExponent(string s)
    {
        if (s.Length == 0) return false;
        var i = s[0] == '+' || s[0] == '-' ? 1 : 0;
        if (i >= s.Length) return false;
        for (; i < s.Length; i++) if (!char.IsAsciiDigit(s[i])) return false;
        return true;
    }

    // ───────────────────────── 解析 + 求值 ─────────────────────────

    private sealed class Parser
    {
        private readonly List<Token> _tokens;
        private readonly int _inputLength;
        private int _pos;
        private int _depth;

        public Parser(List<Token> tokens, int inputLength)
        {
            _tokens = tokens;
            _inputLength = inputLength;
        }

        public bool Ok { get; private set; } = true;
        public string? Error { get; private set; }
        public bool DivisionByZero { get; private set; }
        public bool AtEnd => _pos >= _tokens.Count;
        public int NextPosition => _pos < _tokens.Count ? _tokens[_pos].Position : _inputLength + 1;

        private void Fail(string error)
        {
            if (!Ok) return;
            Ok = false;
            Error = error;
        }

        private Token? Peek() => _pos < _tokens.Count ? _tokens[_pos] : null;

        public double ParseAdditive()
        {
            var left = ParseMultiplicative(out _);
            while (Ok)
            {
                var t = Peek();
                if (t is null) break;
                var isPlus = t.Is(Op.Plus);
                var isMinus = t.Is(Op.Minus);
                if (!isPlus && !isMinus) break;
                _pos++;
                var right = ParseMultiplicative(out var rightPercent);
                // 「200+10%」＝ 200 + 200×0.1；其余场合 right 已是普通数值（10% 在分词后就是 0.1）
                left = rightPercent
                    ? (isPlus ? left + left * right : left - left * right)
                    : (isPlus ? left + right : left - right);
            }
            return left;
        }

        private double ParseMultiplicative(out bool isPercent)
        {
            var left = ParseUnary(out isPercent);
            while (Ok)
            {
                var t = Peek();
                if (t is null) break;
                var isMul = t.Is(Op.Multiply);
                var isDiv = t.Is(Op.Divide);
                if (!isMul && !isDiv) break;
                _pos++;
                var right = ParseUnary(out _);
                left = isMul ? left * right : left / right;
                if (!isMul && right == 0) DivisionByZero = true;
                isPercent = false;   // 乘/除结果不再是"相对左值的百分比"
            }
            return left;
        }

        private double ParseUnary(out bool isPercent)
        {
            var t = Peek();
            if (t is not null && t.Is(Op.Minus))
            {
                _pos++;
                // 符号穿透百分量：「200-10%」的 right 是 +0.1 ⇒ 200-200×0.1=180；
                // 「200--10%」的 right 是 -0.1 ⇒ 200+20。带符号的百分量仍是百分量，故不清标志。
                var v = ParseUnary(out isPercent);
                return -v;
            }
            if (t is not null && t.Is(Op.Plus))
            {
                _pos++;
                return ParseUnary(out isPercent);
            }
            return ParsePower(out isPercent);
        }

        private double ParsePower(out bool isPercent)
        {
            var baseValue = ParsePostfix(out isPercent);
            var t = Peek();
            if (t is not null && t.Is(Op.Power))
            {
                _pos++;
                var exponent = ParseUnary(out _);
                isPercent = false;   // 幂的结果不再是"相对左值的百分比"
                // 0 的负数次幂在数学上就是 1÷0^n：先于 Math.Pow 判定，避免框架返回 ±∞ 时
                // 把"除数为 0"报成含糊的"超出范围"（.NET 在此情形给 NaN 还是 ∞ 不作为依赖）。
                if (baseValue == 0 && exponent < 0)
                {
                    DivisionByZero = true;
                    return double.NaN;
                }
                return Math.Pow(baseValue, exponent);
            }
            return baseValue;
        }

        private double ParsePostfix(out bool isPercent)
        {
            var value = ParsePrimary(out isPercent);
            var applied = false;
            while (Ok)
            {
                var t = Peek();
                if (t is null || t.Kind != TokKind.Percent) break;
                _pos++;
                value /= 100d;
                applied = true;
            }
            if (applied) isPercent = true;
            return value;
        }

        private double ParsePrimary(out bool isPercent)
        {
            isPercent = false;
            var t = Peek();
            if (t is null)
            {
                Fail("表达式不完整：末尾缺少数字");
                return 0;
            }
            if (t.Kind == TokKind.Number)
            {
                _pos++;
                return t.Number;
            }
            if (t.Is(Op.LParen))
            {
                _pos++;
                if (++_depth > MaxDepth)
                {
                    Fail($"括号嵌套过深（上限 {MaxDepth} 层）");
                    return 0;
                }
                var inner = ParseAdditive();
                if (Peek()?.Is(Op.RParen) == true)
                {
                    _pos++;
                    _depth--;
                }
                else
                {
                    Fail("缺少右括号");
                }
                return inner;
            }
            if (t.Is(Op.RParen))
            {
                Fail($"多余的右括号（第 {t.Position} 个字符）");
                _pos++;
                return 0;
            }
            if (t.Kind == TokKind.Percent)
            {
                Fail($"百分号前缺少数字（第 {t.Position} 个字符）");
                _pos++;
                return 0;
            }
            Fail($"这里需要数字（第 {t.Position} 个字符）");
            _pos++;
            return 0;
        }
    }

    // ───────────────────────── 显示 ─────────────────────────

    /// <summary>
    /// 显示串：先以 G15 去掉二进制浮点的十进制噪声（0.1+0.2 → "0.3"，不是 0.30000000000000004），
    /// 再给整数部分加分节号。分节号写法本求值器认得（见 <see cref="IsNumericToken"/>），
    /// 所以"复制结果→粘回去继续算"这条路径不会断。科学计数法串原样返回（加分节号反而错）。
    /// </summary>
    public static string Format(double value)
    {
        if (!double.IsFinite(value)) return value.ToString("G3", CultureInfo.InvariantCulture);
        // -0（如 "0-0"、"-0"）的 G15 是 "-0"，显示成"-0"会让人以为算错了符号
        if (value == 0) return "0";
        var plain = value.ToString("G15", CultureInfo.InvariantCulture);
        if (plain.Contains('E', StringComparison.OrdinalIgnoreCase)) return plain;

        var negative = plain[0] == '-';
        var body = negative ? plain.Substring(1) : plain;
        var dot = body.IndexOf('.');
        var intPart = dot < 0 ? body : body.Substring(0, dot);
        var fracPart = dot < 0 ? string.Empty : body.Substring(dot + 1).TrimEnd('0');

        var grouped = new StringBuilder();
        for (var i = 0; i < intPart.Length; i++)
        {
            if (i > 0 && (intPart.Length - i) % 3 == 0) grouped.Append(',');
            grouped.Append(intPart[i]);
        }
        var text = (grouped.Length == 0 ? "0" : grouped.ToString())
                 + (fracPart.Length == 0 ? string.Empty : "." + fracPart);
        return negative ? "-" + text : text;
    }
}
