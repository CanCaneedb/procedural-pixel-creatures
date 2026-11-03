// Procedural Pixel Creatures - immutable creature genome.
//
// Seed + genome + generator version fully determine a creature. The genome keeps one seed per
// stream (anatomy, color, pattern, motion); the builders for a stream only read the genes and the
// seed of that stream. Changing a color gene therefore can never re-roll the anatomy.
//
// ============================================================================
// 【核心概念：不可变（immutable）】
//
// 基因组一旦创建就不能修改。要"改"一个基因，必须创建一个新的基因组：
//     var g2 = g1.With("size", 0.8);   // g1 不变，g2 是新的
//
// 为什么这样设计？
//   1. 线程安全：工作线程拿到模型后不需要加锁（Java 里用不可变对象同理）
//   2. 撤销/重做简单：每个历史状态都是一个独立对象，不用深拷贝
//   3. 缓存友好：内容哈希不变就说明没变过，可以放心复用
//
// Java 对照：类似 BigDecimal、String、java.time 里的类——操作都返回新对象。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace PixelCreatures.Core.Genetics
{
    /// <summary>
    /// 血统信息：这个生物从哪来、第几代、父母是谁。
    /// <para>用于"子代面板"显示代数，以及杂交时记录每个特征组来自哪个亲代。</para>
    /// </summary>
    public sealed class GenomeLineage
    {
        /// <summary>
        /// 来源，取值："sampled"（种子生成）| "mutation"（变异）| "crossover"（杂交）
        /// | "edited"（手工编辑）| "imported"（导入）。
        /// <para>注意：这些是<b>数据值</b>，会写入存档，不要翻译成中文。</para>
        /// </summary>
        public string Origin { get; }
        /// <summary>第几代。种子生成的是 0，每个子代 +1。</summary>
        public int Generation { get; }
        /// <summary>亲代的 ContentId 列表（杂交时有两个）。</summary>
        public IReadOnlyList<string> ParentIds { get; }
        /// <summary>杂交时每个联动组来自哪个亲代："A"、"B" 或 "A+B"。</summary>
        public IReadOnlyDictionary<string, string> Provenance { get; }
        /// <summary>产生这个子代时使用的变异强度。</summary>
        public double MutationStrength { get; }

        /// <summary>
        /// 构造函数。
        /// <para>
        /// 参数里的 <c>IReadOnlyList&lt;string&gt;? parents = null</c>：
        /// 问号表示"可为 null"，<c>= null</c> 是默认参数值。
        /// Java 没有默认参数，通常得写重载或用 Builder。
        /// </para>
        /// </summary>
        public GenomeLineage(string origin, int generation, IReadOnlyList<string>? parents = null,
            IReadOnlyDictionary<string, string>? provenance = null, double mutationStrength = 0)
        {
            Origin = origin;
            Generation = generation;
            // ?? 是空合并运算符：左边为 null 就用右边。Java 里对应 Optional.orElse。
            ParentIds = parents ?? Array.Empty<string>();
            Provenance = provenance ?? new Dictionary<string, string>();
            MutationStrength = mutationStrength;
        }

        /// <summary>预置的"由种子直接生成"血统，代数为 0。复用一个静态实例，省内存。</summary>
        public static readonly GenomeLineage Sampled = new GenomeLineage("sampled", 0);
    }

    /// <summary>
    /// 一个生物的完整基因数据。
    /// <para>
    /// 【它包含什么】
    /// <list type="bullet">
    /// <item><c>Schema</c>：基因表头（有哪些基因、范围多少）</item>
    /// <item><c>_values</c>：所有基因的值，一个 double 数组</item>
    /// <item><c>_seeds</c>：四个独立的随机种子</item>
    /// <item><c>GeneratorVersion</c>：生成器版本号</item>
    /// <item><c>Lineage</c>：血统</item>
    /// </list>
    /// </para>
    /// <para>
    /// 【为什么版本号很重要】算法改了（比如改进了腿部生成公式），同一种子生成的生物会变。
    /// 版本号让加载旧存档时能检测到"这是用旧算法生成的"，从而给出警告而不是静默改变外观。
    /// </para>
    /// </summary>
    public sealed class CreatureGenome
    {
        /// <summary>当前存档格式版本。改结构时递增，并写一个迁移。</summary>
        public const int CurrentFormatVersion = 1;

        /// <summary>
        /// 所有基因的值，按 Schema 的顺序排列。
        /// <para>
        /// <c>private readonly</c>：外部不能直接改这个数组，只能通过 <c>Get</c>/<c>With</c> 访问。
        /// 这是不可变性的保障之一（虽然数组内容技术上仍可变，但项目内从不这样用）。
        /// </para>
        /// </summary>
        private readonly double[] _values;

        /// <summary>四个流的种子，顺序对应 <see cref="GeneStream"/> 枚举：结构/颜色/花纹/动作。</summary>
        private readonly ulong[] _seeds;

        /// <summary>所属家族 id（如 "quadruped"）。从 Schema 取得。</summary>
        public string FamilyId { get; }
        /// <summary>基因表头。</summary>
        public GeneSchema Schema { get; }
        /// <summary>生成器版本。见类注释。</summary>
        public int GeneratorVersion { get; }
        /// <summary>生物名字（可空字符串，可含中文）。</summary>
        public string Name { get; }
        /// <summary>血统。</summary>
        public GenomeLineage Lineage { get; }

        /// <summary>
        /// 构造基因组。会做两件重要的规范化：
        /// <list type="number">
        /// <item>克隆种子数组（防止调用方之后改它）</item>
        /// <item>对每个基因值调用 Quantize（保证在网格上）</item>
        /// </list>
        /// </summary>
        public CreatureGenome(GeneSchema schema, int generatorVersion, ulong[] seeds, double[] values, string name, GenomeLineage? lineage = null)
        {
            // 参数校验。ArgumentException 是 C# 内置异常，对应 Java 的 IllegalArgumentException。
            // nameof(seeds) 是编译期取得参数名的技巧，重构时不会写错。
            if (seeds.Length != 4) throw new ArgumentException("A genome needs exactly four stream seeds.", nameof(seeds));
            if (values.Length != schema.Count) throw new ArgumentException($"Expected {schema.Count} gene values, got {values.Length}.", nameof(values));
            Schema = schema;
            FamilyId = schema.FamilyId;
            GeneratorVersion = generatorVersion;
            // 防御性拷贝：如果直接保存 seeds 引用，调用方之后修改它就会破坏不可变性
            _seeds = (ulong[])seeds.Clone();
            _values = new double[values.Length];
            for (int i = 0; i < values.Length; i++) _values[i] = schema.Genes[i].Quantize(values[i]);
            Name = name ?? string.Empty;
            Lineage = lineage ?? GenomeLineage.Sampled;
        }

        /// <summary>
        /// 创建一个"全部使用默认值"的基因组。常用于测试和新建空白生物。
        /// </summary>
        public static CreatureGenome CreateDefault(GeneSchema schema, int generatorVersion, ulong seed, string name = "")
        {
            var values = new double[schema.Count];
            for (int i = 0; i < values.Length; i++) values[i] = schema.Genes[i].Default;
            return new CreatureGenome(schema, generatorVersion, DeriveStreamSeeds(seed), values, name);
        }

        /// <summary>
        /// 从主种子派生四个流种子。这是"四路独立"的实现位置。
        /// <para>
        /// <c>=&gt; new[] { ... }</c> 是表达式体方法，等价于：
        /// <code>
        /// public static ulong[] DeriveStreamSeeds(ulong masterSeed) {
        ///     return new[] { ... };
        /// }
        /// </code>
        /// </para>
        /// <para>
        /// 关键：每个种子用不同的标签字符串（"stream.anatomy" 等）派生，
        /// 所以四个种子在统计上互相独立。改颜色种子不会影响结构种子。
        /// </para>
        /// </summary>
        public static ulong[] DeriveStreamSeeds(ulong masterSeed) => new[]
        {
            StableHash.Derive(masterSeed, "stream.anatomy"),
            StableHash.Derive(masterSeed, "stream.color"),
            StableHash.Derive(masterSeed, "stream.pattern"),
            StableHash.Derive(masterSeed, "stream.motion"),
        };

        /// <summary>取某个流的种子。<c>(int)stream</c> 把枚举转成下标。</summary>
        public ulong Seed(GeneStream stream) => _seeds[(int)stream];
        /// <summary>结构种子。</summary>
        public ulong AnatomySeed => _seeds[0];
        /// <summary>颜色种子。</summary>
        public ulong ColorSeed => _seeds[1];
        /// <summary>花纹种子。</summary>
        public ulong PatternSeed => _seeds[2];
        public ulong MotionSeed => _seeds[3];
        public ulong[] CopySeeds() => (ulong[])_seeds.Clone();
        public double[] CopyValues() => (double[])_values.Clone();

        public int Count => _values.Length;
        public double this[int index] => _values[index];

        public double Get(string id)
        {
            int i = Schema.IndexOf(id);
            if (i < 0) throw new KeyNotFoundException($"Gene '{id}' is not part of family '{FamilyId}'.");
            return _values[i];
        }

        public double GetOr(string id, double fallback)
        {
            int i = Schema.IndexOf(id);
            return i < 0 ? fallback : _values[i];
        }

        public int GetInt(string id) => (int)Math.Round(Get(id));
        public bool GetBool(string id) => Get(id) >= 0.5;

        public string GetChoice(string id)
        {
            var def = Schema.Get(id);
            int i = (int)Get(id);
            return i >= 0 && i < def.Options.Count ? def.Options[i].Id : def.Options[0].Id;
        }

        public CreatureGenome With(string id, double value, GenomeLineage? lineage = null)
        {
            int i = Schema.IndexOf(id);
            if (i < 0) throw new KeyNotFoundException($"Gene '{id}' is not part of family '{FamilyId}'.");
            var v = CopyValues();
            v[i] = value;
            return new CreatureGenome(Schema, GeneratorVersion, _seeds, v, Name, lineage ?? Lineage);
        }

        public CreatureGenome WithChoice(string id, string optionId)
        {
            var def = Schema.Get(id);
            int idx = def.OptionIndex(optionId);
            if (idx < 0) throw new ArgumentException($"Option '{optionId}' is not valid for gene '{id}'.");
            return With(id, idx);
        }

        public CreatureGenome WithValues(double[] values, GenomeLineage? lineage = null) =>
            new CreatureGenome(Schema, GeneratorVersion, _seeds, values, Name, lineage ?? Lineage);

        public CreatureGenome WithSeeds(ulong[] seeds, GenomeLineage? lineage = null) =>
            new CreatureGenome(Schema, GeneratorVersion, seeds, _values, Name, lineage ?? Lineage);

        public CreatureGenome WithSeed(GeneStream stream, ulong seed)
        {
            var s = CopySeeds();
            s[(int)stream] = seed;
            return new CreatureGenome(Schema, GeneratorVersion, s, _values, Name, Lineage);
        }

        public CreatureGenome WithName(string name) => new CreatureGenome(Schema, GeneratorVersion, _seeds, _values, name, Lineage);

        public CreatureGenome WithLineage(GenomeLineage lineage) => new CreatureGenome(Schema, GeneratorVersion, _seeds, _values, Name, lineage);

        public CreatureGenome WithGeneratorVersion(int version) => new CreatureGenome(Schema, version, _seeds, _values, Name, Lineage);

        // ------------------------------------------------------------------ identity hashes

        private ulong[]? _streamKeys;

        /// <summary>Hash over everything that can influence one stream's output (cached; genomes are immutable).</summary>
        public ulong StreamKey(GeneStream stream)
        {
            var keys = _streamKeys;
            if (keys == null)
            {
                keys = new ulong[4];
                for (int s = 0; s < 4; s++) keys[s] = ComputeStreamKey((GeneStream)s);
                _streamKeys = keys;
            }
            return keys[(int)stream];
        }

        private ulong ComputeStreamKey(GeneStream stream)
        {
            ulong h = StableHash.Fnv1a64(FamilyId);
            h = StableHash.Combine(h, (ulong)GeneratorVersion);
            h = StableHash.Combine(h, Schema.Fingerprint());
            h = StableHash.Combine(h, (ulong)stream);
            h = StableHash.Combine(h, _seeds[(int)stream]);
            for (int i = 0; i < _values.Length; i++)
            {
                if (Schema.Genes[i].Stream != stream) continue;
                h = StableHash.Combine(h, (ulong)BitConverter.DoubleToInt64Bits(_values[i]));
            }
            return h;
        }

        public ulong AnatomyKey => StreamKey(GeneStream.Anatomy);
        public ulong ColorKey => StreamKey(GeneStream.Color);
        public ulong PatternKey => StreamKey(GeneStream.Pattern);
        public ulong MotionKey => StreamKey(GeneStream.Motion);

        /// <summary>Identity of the whole genome (name and lineage excluded; they do not change the creature).</summary>
        public ulong ContentHash
        {
            get
            {
                ulong h = AnatomyKey;
                h = StableHash.Combine(h, ColorKey);
                h = StableHash.Combine(h, PatternKey);
                h = StableHash.Combine(h, MotionKey);
                return h;
            }
        }

        public string ContentId => ContentHash.ToString("x16", CultureInfo.InvariantCulture);

        public bool ContentEquals(CreatureGenome other)
        {
            if (other.FamilyId != FamilyId || other.GeneratorVersion != GeneratorVersion || other._values.Length != _values.Length) return false;
            for (int i = 0; i < _seeds.Length; i++) if (_seeds[i] != other._seeds[i]) return false;
            for (int i = 0; i < _values.Length; i++) if (_values[i] != other._values[i]) return false;
            return true;
        }

        public override string ToString() => $"{FamilyId}:{ContentId} '{Name}'";
    }
}
