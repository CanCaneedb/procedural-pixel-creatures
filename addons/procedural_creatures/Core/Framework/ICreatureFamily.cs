// Procedural Pixel Creatures - body plan families (extension point).
//
// A family owns a gene schema, samples genomes from seeds (with archetypes as priors) and builds
// anatomies. It never touches colours or animation directly: colours come from palette rules,
// animation from motion modules selected through the anatomy's MotionRig.
//
// ============================================================================
// 【这一层的作用：定义"体型类别"】
//
// 项目里有 9 个类别：四足、双足、爬行、节肢、有翼、蛇、水生、史莱姆、植物族。
// 每个类别负责三件事：
//   1. 声明自己的基因表        → CreateSchema()
//   2. 从种子采样基因值        → SampleAnatomy()
//   3. 从基因值造出骨架        → BuildAnatomy()
//
// 【关键设计】类别只管"结构"，不碰颜色和动画：
//   颜色由 PaletteRules 按 StyleHints 生成
//   动画由 MotionRig + 运动模块驱动
// 这样加一个新类别不用重写配色和动画系统。
//
// 【Java 对照】这就是一个标准的"策略模式"（Strategy）：
//   ICreatureFamily 是接口，CreatureFamilyBase 提供公共实现，
//   具体家族继承基类。和你写 Java 时用 abstract class + 子类一模一样。
// ============================================================================

using System;
using System.Collections.Generic;
using PixelCreatures.Core.Anatomy;
using PixelCreatures.Core.Genetics;

namespace PixelCreatures.Core
{
    /// <summary>
    /// 一个"原型"（archetype）：某个类别下的具体形态，比如四足类下的"犬科""猫科""牛科"。
    /// <para>
    /// 原型决定了采样的"先验"（prior）——选"猫科"时，体型会偏向小巧、耳朵偏尖、瞳孔偏竖。
    /// 这让生成的生物看起来像"某种动物"，而不是随机拼凑。
    /// </para>
    /// </summary>
    public sealed class ArchetypeInfo
    {
        /// <summary>稳定 id，例如 "feline"。会写入基因值（form 基因），不要改。</summary>
        public string Id { get; }
        /// <summary>显示名，例如 "猫科"。可翻译。</summary>
        public string Label { get; }
        /// <summary>随机选中这个原型的权重（相对值，不是百分比）。</summary>
        public double Weight { get; }

        /// <summary>
        /// 构造函数。注意 <c>double weight = 1</c>：默认参数，调用时可以只传两个参数。
        /// Java 里没有默认参数，得写重载方法。
        /// </summary>
        public ArchetypeInfo(string id, string label, double weight = 1) { Id = id; Label = label; Weight = weight; }
    }

    /// <summary>
    /// 体型类别的接口 —— 所有家族必须实现的契约。
    /// <para>
    /// 【Java 对照】这就是一个 interface，含义完全一样：
    /// 定义"要提供哪些能力"，不规定"怎么实现"。
    /// </para>
    /// <para>
    /// 【注意注释里的警告】<c>Id</c> 会被写进基因组存档，
    /// 改名会导致旧存档加载失败。要改就得同时写一个迁移（见 GenomeSerializer）。
    /// 这条规则在整个项目里反复出现，是数据兼容性的底线。
    /// </para>
    /// </summary>
    public interface ICreatureFamily
    {
        /// <summary>稳定 id（如 "quadruped"），写入存档。永远不要改，要改就加迁移。</summary>
        string Id { get; }
        /// <summary>界面显示名（如 "四足兽类"）。可以翻译。</summary>
        string DisplayName { get; }
        /// <summary>类别描述，显示在悬停提示里。</summary>
        string Description { get; }
        /// <summary>界面里的排序权重。</summary>
        int Order { get; }
        /// <summary>这个类别的基因表头。</summary>
        GeneSchema Schema { get; }
        /// <summary>可选的原型列表（如犬科/猫科/牛科）。</summary>
        IReadOnlyList<ArchetypeInfo> Archetypes { get; }
        /// <summary>使用的配色规则 id，见 CreatureRegistry。</summary>
        string PaletteRuleId { get; }
        /// <summary>某个原型的风格倾向，用来偏置颜色/花纹/动作的采样。</summary>
        FamilyStyleHints StyleHints(string archetype);
        /// <summary>采样本类别独有的（结构）基因。颜色/花纹/动作由 SharedGenes 统一采样。</summary>
        void SampleAnatomy(double[] values, Rng rng, string archetype);
        /// <summary>共享基因采样完之后的最后调整机会（例如魔像偏向石质材质）。</summary>
        void AdjustSample(double[] values, Rng rng, string archetype);
        /// <summary>根据基因组造出骨架。只应读取结构流的基因。</summary>
        CreatureAnatomy BuildAnatomy(CreatureGenome genome, CreatureRegistry registry);
    }

    /// <summary>
    /// 家族的抽象基类 —— 把各家族共用的部分实现好，子类只需填自己的内容。
    /// <para>
    /// 【C# vs Java】<c>abstract class</c> 完全对应 Java 的 abstract class：
    /// 不能直接 new，只能被继承；<c>abstract</c> 方法子类必须实现；
    /// <c>virtual</c> 方法有默认实现、子类可覆盖（Java 的默认实现方法）。
    /// </para>
    /// <para>
    /// 【用 abstract class 还是 interface？】项目里的约定：
    /// interface 描述能力（ICreatureFamily），abstract class 提供复用（CreatureFamilyBase）。
    /// 和 Java 的最佳实践一致。
    /// </para>
    /// </summary>
    public abstract class CreatureFamilyBase : ICreatureFamily
    {
        /// <summary>
        /// 缓存的基因表。第一次访问 Schema 时才创建（懒加载）。
        /// <para><c>GeneSchema?</c> 的问号表示"可能为 null"，这是 C# 8 的可空引用类型检查。</para>
        /// </summary>
        private GeneSchema? _schema;

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public abstract string Description { get; }

        /// <summary>
        /// 默认排序权重。<c>virtual</c> 表示子类可以覆盖；不覆盖就用 100。
        /// <para>Java 对照：普通方法（可被子类 override）。</para>
        /// </summary>
        public virtual int Order => 100;
        public virtual string PaletteRuleId => "default";
        public abstract IReadOnlyList<ArchetypeInfo> Archetypes { get; }

        /// <summary>
        /// 懒加载 Schema：第一次访问时调用 CreateSchema()，之后直接返回缓存。
        /// <para>
        /// <c>_schema ??= CreateSchema()</c> 读作"如果 _schema 为 null，就赋值为 CreateSchema()"。
        /// 这是 C# 的空合并赋值运算符，Java 没有等价语法（得写 if 判断）。
        /// </para>
        /// <para>
        /// 为什么要懒加载？因为 Schema 构建要跑不少代码，而某些场景（如只读 Id）根本用不到它。
        /// </para>
        /// </summary>
        public GeneSchema Schema => _schema ??= CreateSchema();

        /// <summary>
        /// 子类实现这个方法，用 GeneSchemaBuilder 声明自己的基因。
        /// <para>颜色/花纹/动作基因调用 SharedGenes.Add* 添加（所有类别共用）。</para>
        /// </summary>
        protected abstract GeneSchema CreateSchema();

        public abstract FamilyStyleHints StyleHints(string archetype);
        public abstract void SampleAnatomy(double[] values, Rng rng, string archetype);

        /// <summary>
        /// 可选的采样后调整。<c>virtual</c> + 空方法体 = 子类可以不实现。
        /// <para>Java 对照：一个有默认空实现的普通方法。</para>
        /// </summary>
        public virtual void AdjustSample(double[] values, Rng rng, string archetype) { }

        public abstract CreatureAnatomy BuildAnatomy(CreatureGenome genome, CreatureRegistry registry);

        /// <summary>
        /// 便捷方法：按 id 设置基因值。
        /// <para><c>=&gt;</c> 表达式体，等价于 <c>{ SharedGenes.Set(...); }</c>。</para>
        /// <para>
        /// <c>protected</c> 表示"只有自己和子类能调用"（Java 的 protected 一样）。
        /// </para>
        /// </summary>
        protected void Set(double[] values, string id, double v) => SharedGenes.Set(Schema, values, id, v);

        /// <summary>便捷方法：按选项 id 设置 Choice 基因（内部转成索引存储）。</summary>
        protected void SetChoice(double[] values, string id, string option)
        {
            var def = Schema.Get(id);
            int i = def.OptionIndex(option);
            if (i < 0) throw new ArgumentException($"Unknown option '{option}' for '{id}'.");
            values[Schema.IndexOf(id)] = i;
        }

        /// <summary>
        /// 围绕某个中心值做高斯采样，再限制到范围内。
        /// <para>用于"这个原型通常是 X 左右，但允许有偏差"的场合。</para>
        /// </summary>
        protected void Around(double[] values, Rng rng, string id, double centre, double spread)
        {
            var def = Schema.Get(id);
            double v = rng.Gaussian(centre, spread, 2.2);
            values[Schema.IndexOf(id)] = def.Quantize(v);
        }

        /// <summary>
        /// 按权重随机选一个选项。
        /// <para>
        /// <c>params (string id, double w)[] options</c> 里：
        /// <list type="bullet">
        /// <item><c>params</c> 允许调用时直接传多个参数而不用建数组</item>
        /// <item><c>(string id, double w)</c> 是 C# 的元组（tuple），Java 没有内置元组</item>
        /// </list>
        /// 调用示例：<c>PickWeighted(rng, ("pointed", 3.0), ("round", 1.0))</c>
        /// </para>
        /// </summary>
        protected static string PickWeighted(Rng rng, params (string id, double w)[] options)
        {
            var weights = new List<double>();
            foreach (var o in options) weights.Add(o.w);
            return options[rng.Weighted(weights)].id;
        }
    }

    /// <summary>
    /// 扩展点：外部插件可以往注册表里加自己的家族、形状块、配色规则、运动模块。
    /// <para>植物族（procedural_creatures_plantfolk）就是用它实现的。</para>
    /// </summary>
    public interface ICreatureExtension
    {
        string Id { get; }
        void Register(CreatureRegistry registry);
    }

    /// <summary>
    /// 标记一个类是"扩展"。加了它的类会被自动发现并调用 Register。
    /// <para>
    /// 【C# vs Java】<c>[CreatureExtension]</c> 是 C# 的 Attribute（用方括号），
    /// 对应 Java 的 <c>@Annotation</c>（用 @ 符号）。
    /// </para>
    /// <para>
    /// <c>[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]</c> 限制它只能标在类上、
    /// 且每个类只能用一次。
    /// </para>
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class CreatureExtensionAttribute : Attribute { }
}
