// Procedural Pixel Creatures - gene schema.
//
// Every family describes its genome with a schema: an ordered list of typed gene definitions.
// The schema drives validation, serialization, mutation, crossover, locking and the automatically
// generated workshop controls. Values are stored as doubles; choice genes store the option index,
// bools 0/1, ints integral values. Float genes are quantized to a fixed grid so that a value that
// survives a JSON round trip is bit identical to the value used for generation.
//
// ============================================================================
// 【这一层在整个项目中的位置】
//
//   种子 ──► 基因组(基因值数组) ──► 骨架/颜色/花纹/动作 ──► 像素
//              ▲
//              └── GeneSchema 定义了"基因组里有哪些格子"
//
// 打个比方：GeneSchema 是"表格的表头"（有哪些字段、范围多少、什么类型），
// CreatureGenome 是"填好的一行数据"。
//
// 【Java 开发者注意】
// 本项目大量使用"属性"（property）：
//     public string Id { get; }        // 只读，调用时写 obj.Id（不带括号）
// 这等价于 Java 的：
//     private final String id;
//     public String getId() { return id; }
// 但调用语法更简洁。看到 { get; } 就当它是"只读字段"。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PixelCreatures.Core.Genetics
{
    /// <summary>
    /// 基因所属的"数据流"。这是本项目最核心的设计之一。
    /// <para>
    /// 每个流有自己独立的随机种子。因此：
    /// <list type="bullet">
    /// <item>改颜色基因，绝不可能影响体型（两者用不同种子、不同流）</item>
    /// <item>重掷花纹，体型和颜色保持原样</item>
    /// </list>
    /// 这让"局部重掷"变得可预测，也让缓存能精确失效（只重建受影响的那部分）。
    /// </para>
    /// <para>
    /// 【C# vs Java】这里给枚举显式赋值 0..3，是为了能安全地强转成数组下标：
    /// <c>_seeds[(int)stream]</c>。Java 的 enum 是对象，得用 ordinal()。
    /// </para>
    /// </summary>
    public enum GeneStream
    {
        /// <summary>结构：骨骼、肢体、头部形状。影响"长什么样"。</summary>
        Anatomy = 0,
        /// <summary>颜色：色相、饱和度、明度、配色方案。</summary>
        Color = 1,
        /// <summary>花纹：条纹/斑点、表面材质、纹理。</summary>
        Pattern = 2,
        /// <summary>动作：活力、重量感、刚度、弹跳。</summary>
        Motion = 3,
    }

    /// <summary>
    /// 基因的值类型。决定了在编辑器里用什么控件，以及如何变异/遗传。
    /// </summary>
    public enum GeneKind
    {
        /// <summary>浮点数（0..1 之类）。编辑器显示为滑块。</summary>
        Float,
        /// <summary>整数（如腿数 4）。编辑器显示为滑块（步长为 1）。</summary>
        Int,
        /// <summary>选项（如"尖耳/圆耳"）。存储的是选项的索引。编辑器显示为下拉框。</summary>
        Choice,
        /// <summary>布尔（如"是否有鬃毛"）。编辑器显示为复选框。存储为 0 或 1。</summary>
        Bool,
    }

    /// <summary>
    /// 杂交时该基因如何从双亲组合。
    /// <para>这是"遗传学"语义的核心，决定了子代像谁。</para>
    /// </summary>
    public enum InheritanceMode
    {
        /// <summary>在两个亲代的值之间随机插值。适合连续性状（体型、长度）。</summary>
        Blend,
        /// <summary>直接取某一亲代的值。适合离散性状（耳型），也用于联动组（整组一起继承）。</summary>
        Pick,
        /// <summary>色相这种"环形"值：沿最短弧插值（350° 和 10° 之间是 0°，不是 180°）。</summary>
        Circular,
    }

    /// <summary>编辑器里显示的单位，只影响显示，不影响存储。</summary>
    public enum DisplayUnit
    {
        Percent,
        Degrees,
        Plain,
        Count,
    }

    /// <summary>
    /// 一个选项（用于 <see cref="GeneKind.Choice"/> 类型的基因）。
    /// <para>
    /// <c>Id</c> 会被写进存档，所以<b>永远不要改</b>；<c>Label</c> 只用于显示，可以随便改。
    /// 这条规则在本项目里到处适用：id 稳定、label 可改。
    /// </para>
    /// </summary>
    public sealed class GeneOption
    {
        /// <summary>稳定的标识，会写入 JSON 存档。不要改。</summary>
        public string Id { get; }
        /// <summary>显示名，可以翻译/修改，不影响存档兼容性。</summary>
        public string Label { get; }
        public GeneOption(string id, string label) { Id = id; Label = label; }

        /// <summary>
        /// 重写 ToString，让调试输出更可读。
        /// <para>Java 对照：<c>@Override public String toString()</c>。</para>
        /// </summary>
        public override string ToString() => Id;
    }

    /// <summary>
    /// 一个基因的完整定义（"表头"里的一列）。
    /// <para>
    /// 注意 <c>sealed</c> 关键字：表示这个类不能被继承。
    /// Java 里对应 <c>final class</c>。项目里几乎所有数据类都 sealed，
    /// 好处是编译器能做更好的优化，也表明"这不是给别人扩展用的"。
    /// </para>
    /// </summary>
    public sealed class GeneDef
    {
        /// <summary>稳定标识，写入存档。例如 "torso.length"。不要改。</summary>
        public string Id { get; }
        /// <summary>显示名，例如 "躯干长度"。可以改。</summary>
        public string Label { get; }
        /// <summary>悬停提示里的说明文字。</summary>
        public string Description { get; }
        /// <summary>所属分组 id（如 "body"），用于编辑器分节显示。</summary>
        public string Group { get; }
        /// <summary>所属数据流，决定它影响哪个阶段。</summary>
        public GeneStream Stream { get; }
        /// <summary>值类型。</summary>
        public GeneKind Kind { get; }
        /// <summary>最小值。</summary>
        public double Min { get; }
        /// <summary>最大值。</summary>
        public double Max { get; }
        /// <summary>默认值（构造时已经过 Quantize 处理）。</summary>
        public double Default { get; }
        /// <summary>量化步长。浮点数会被"吸附"到这个网格上，保证存取一致。</summary>
        public double Step { get; }
        /// <summary>变异强度为 1 时的高斯标准差，以值域的比例表示。</summary>
        public double MutationSigma { get; }
        /// <summary>杂交时的继承方式。</summary>
        public InheritanceMode Inheritance { get; }
        /// <summary>
        /// 联动组 key。同一 key 的基因在杂交时会"整组一起"来自同一亲代，
        /// 避免出现解剖上矛盾的组合（例如"长颈"配"短颈肌肉"）。
        /// </summary>
        public string Linkage { get; }
        /// <summary>Choice 类型的可选项列表；其他类型为空。</summary>
        public IReadOnlyList<GeneOption> Options { get; }
        /// <summary>显示单位。</summary>
        public DisplayUnit Unit { get; }
        /// <summary>标记为"高级"的基因在紧凑视图里隐藏。</summary>
        public bool Advanced { get; }
        /// <summary>定义顺序，决定编辑器里的排列。</summary>
        public int Order { get; }

        /// <summary>
        /// 构造函数。
        /// <para>
        /// <c>internal</c> 表示"只能在同一个程序集内调用"——外部代码必须通过
        /// <see cref="GeneSchemaBuilder"/> 来创建基因，不能直接 new。这是一种封装手段。
        /// </para>
        /// <para>
        /// 注意最后一行 <c>Default = Quantize(def);</c>：
        /// 默认值也要量化，否则默认值和量化后的值可能不一致，导致"新建生物"和"重载生物"结果不同。
        /// </para>
        /// </summary>
        internal GeneDef(string id, string label, string description, string group, GeneStream stream, GeneKind kind,
            double min, double max, double def, double step, double sigma, InheritanceMode inheritance, string linkage,
            IReadOnlyList<GeneOption> options, DisplayUnit unit, bool advanced, int order)
        {
            Id = id; Label = label; Description = description; Group = group; Stream = stream; Kind = kind;
            Min = min; Max = max; Step = step; MutationSigma = sigma; Inheritance = inheritance; Linkage = linkage;
            Options = options; Unit = unit; Advanced = advanced; Order = order;
            Default = Quantize(def);
        }

        /// <summary>值域宽度（Max - Min）。变异强度按这个宽度换算成绝对值。</summary>
        public double Range => Max - Min;

        /// <summary>
        /// 把值限制在范围内，并吸附到量化网格上。纯函数，无副作用。
        /// <para>
        /// 【为什么必须量化？】浮点数存进 JSON 再读出来可能差最后一位（如 0.123456789 → 0.123456788）。
        /// 本项目承诺"同一种子必然得到同一生物"，所以要把值吸附到固定网格，
        /// 确保"内存里的值"和"存盘再读回的值"完全一致。
        /// </para>
        /// </summary>
        public double Quantize(double v)
        {
            if (double.IsNaN(v)) v = Default;
            if (v < Min) v = Min;
            if (v > Max) v = Max;
            switch (Kind)
            {
                case GeneKind.Float:
                {
                    double k = Math.Round((v - Min) / Step, MidpointRounding.AwayFromZero);
                    double q = Min + k * Step;
                    if (q > Max) q = Max;
                    if (q < Min) q = Min;
                    return q;
                }
                case GeneKind.Bool:
                    return v >= 0.5 ? 1.0 : 0.0;
                default:
                    return Math.Round(v, MidpointRounding.AwayFromZero);
            }
        }

        public int OptionIndex(string id)
        {
            for (int i = 0; i < Options.Count; i++)
                if (string.Equals(Options[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        public string FormatValue(double v)
        {
            switch (Kind)
            {
                case GeneKind.Choice:
                {
                    int i = (int)v;
                    return i >= 0 && i < Options.Count ? Options[i].Label : "?";
                }
                case GeneKind.Bool:
                    return v >= 0.5 ? "ja" : "nein";
                case GeneKind.Int:
                    return ((int)v).ToString(CultureInfo.InvariantCulture);
                default:
                    switch (Unit)
                    {
                        case DisplayUnit.Percent: return (v * 100.0).ToString("0", CultureInfo.InvariantCulture) + " %";
                        case DisplayUnit.Degrees: return v.ToString("0", CultureInfo.InvariantCulture) + "°";
                        default: return v.ToString("0.00", CultureInfo.InvariantCulture);
                    }
            }
        }

        public override string ToString() => Id;
    }

    public sealed class GeneGroupDef
    {
        public string Id { get; }
        public string Label { get; }
        public GeneStream Stream { get; }
        public GeneGroupDef(string id, string label, GeneStream stream) { Id = id; Label = label; Stream = stream; }
    }

    public sealed class GeneSchema
    {
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>(StringComparer.Ordinal);

        public string FamilyId { get; }
        public IReadOnlyList<GeneDef> Genes { get; }
        public IReadOnlyList<GeneGroupDef> Groups { get; }

        internal GeneSchema(string familyId, List<GeneDef> genes, List<GeneGroupDef> groups)
        {
            FamilyId = familyId;
            Genes = genes;
            Groups = groups;
            for (int i = 0; i < genes.Count; i++)
            {
                if (_index.ContainsKey(genes[i].Id))
                    throw new InvalidOperationException($"Duplicate gene id '{genes[i].Id}' in schema '{familyId}'.");
                _index[genes[i].Id] = i;
            }
        }

        public int Count => Genes.Count;

        public int IndexOf(string id) => _index.TryGetValue(id, out int i) ? i : -1;

        public bool Contains(string id) => _index.ContainsKey(id);

        public GeneDef Get(string id)
        {
            if (!_index.TryGetValue(id, out int i)) throw new KeyNotFoundException($"Gene '{id}' is not part of schema '{FamilyId}'.");
            return Genes[i];
        }

        public GeneGroupDef? GetGroup(string id)
        {
            foreach (var g in Groups) if (g.Id == id) return g;
            return null;
        }

        public IEnumerable<GeneDef> GenesInGroup(string groupId)
        {
            foreach (var g in Genes) if (g.Group == groupId) yield return g;
        }

        /// <summary>Stable fingerprint of the schema layout (ids, kinds, ranges). Used in cache keys and tests.</summary>
        private ulong _fingerprint;
        private bool _hasFingerprint;

        /// <summary>Stable hash of the schema layout (cached: a schema never changes after Build).</summary>
        public ulong Fingerprint()
        {
            if (_hasFingerprint) return _fingerprint;
            _fingerprint = ComputeFingerprint();
            _hasFingerprint = true;
            return _fingerprint;
        }

        private ulong ComputeFingerprint()
        {
            ulong h = StableHash.Fnv1a64(FamilyId);
            foreach (var g in Genes)
            {
                h = StableHash.Combine(h, StableHash.Fnv1a64(g.Id));
                h = StableHash.Combine(h, (ulong)g.Kind);
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(g.Min));
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(g.Max));
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(g.Step));
                foreach (var o in g.Options) h = StableHash.Combine(h, StableHash.Fnv1a64(o.Id));
            }
            return h;
        }
    }

    /// <summary>Fluent builder used by families (and extensions) to declare their genes.</summary>
    public sealed class GeneSchemaBuilder
    {
        private readonly string _familyId;
        private readonly List<GeneDef> _genes = new List<GeneDef>();
        private readonly List<GeneGroupDef> _groups = new List<GeneGroupDef>();
        private string _group = "general";
        private GeneStream _stream = GeneStream.Anatomy;

        public GeneSchemaBuilder(string familyId) { _familyId = familyId; }

        public GeneSchemaBuilder Group(string id, string label, GeneStream stream)
        {
            _group = id;
            _stream = stream;
            bool exists = false;
            foreach (var g in _groups) if (g.Id == id) exists = true;
            if (!exists) _groups.Add(new GeneGroupDef(id, label, stream));
            return this;
        }

        public GeneSchemaBuilder Float(string id, string label, double min, double max, double def,
            string description = "", double sigma = 0.10, InheritanceMode inheritance = InheritanceMode.Blend,
            string? linkage = null, DisplayUnit unit = DisplayUnit.Percent, bool advanced = false, double? step = null)
        {
            double s = step ?? (max - min) / 1000.0;
            _genes.Add(new GeneDef(id, label, description, _group, _stream, GeneKind.Float, min, max, def, s, sigma,
                inheritance, linkage ?? _group, Array.Empty<GeneOption>(), unit, advanced, _genes.Count));
            return this;
        }

        public GeneSchemaBuilder Int(string id, string label, int min, int max, int def,
            string description = "", double sigma = 0.15, string? linkage = null, bool advanced = false)
        {
            _genes.Add(new GeneDef(id, label, description, _group, _stream, GeneKind.Int, min, max, def, 1.0, sigma,
                InheritanceMode.Pick, linkage ?? _group, Array.Empty<GeneOption>(), DisplayUnit.Count, advanced, _genes.Count));
            return this;
        }

        public GeneSchemaBuilder Choice(string id, string label, (string id, string label)[] options, string def,
            string description = "", double sigma = 0.12, string? linkage = null, bool advanced = false)
        {
            var opts = new List<GeneOption>();
            int defIndex = 0;
            for (int i = 0; i < options.Length; i++)
            {
                opts.Add(new GeneOption(options[i].id, options[i].label));
                if (options[i].id == def) defIndex = i;
            }
            _genes.Add(new GeneDef(id, label, description, _group, _stream, GeneKind.Choice, 0, opts.Count - 1, defIndex, 1.0, sigma,
                InheritanceMode.Pick, linkage ?? _group, opts, DisplayUnit.Plain, advanced, _genes.Count));
            return this;
        }

        public GeneSchemaBuilder Bool(string id, string label, bool def, string description = "", double sigma = 0.10,
            string? linkage = null, bool advanced = false)
        {
            _genes.Add(new GeneDef(id, label, description, _group, _stream, GeneKind.Bool, 0, 1, def ? 1 : 0, 1.0, sigma,
                InheritanceMode.Pick, linkage ?? _group, Array.Empty<GeneOption>(), DisplayUnit.Plain, advanced, _genes.Count));
            return this;
        }

        public GeneSchemaBuilder Hue(string id, string label, double def, string description = "", double sigma = 0.06, string? linkage = null)
        {
            _genes.Add(new GeneDef(id, label, description, _group, _stream, GeneKind.Float, 0, 360, def, 0.5, sigma,
                InheritanceMode.Circular, linkage ?? _group, Array.Empty<GeneOption>(), DisplayUnit.Degrees, false, _genes.Count));
            return this;
        }

        public GeneSchema Build() => new GeneSchema(_familyId, new List<GeneDef>(_genes), new List<GeneGroupDef>(_groups));
    }
}
