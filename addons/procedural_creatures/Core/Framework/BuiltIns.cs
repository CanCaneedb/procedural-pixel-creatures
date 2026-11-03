// Procedural Pixel Creatures - registration of all built-in content.
//
// ============================================================================
// 【建议从这里开始读代码】
//
// 这个文件只有 30 多行，但它列出了项目的全部"可插拔部件"：
//   配色规则、材质、形状块、6 种运动模块、8 个内置家族
//
// 读完这个文件，你就知道项目里有哪些零件，然后挑感兴趣的去看。
//
// 【它做的事：注册表模式（Registry Pattern）】
// 与其在各处硬编码 "if (family == "quadruped") ..."，
// 不如把家族对象登记到一个注册表里，用 id 查找。
// 好处：加新家族不用改任何已有代码。
//
// 【Java 对照】和 Spring 的 ApplicationContext 注册 Bean 思路类似，
// 只是这里是显式调用，没有反射和注解扫描。
// ============================================================================

using PixelCreatures.Core.Families;
using PixelCreatures.Core.Motion;
using PixelCreatures.Core.Palettes;
using PixelCreatures.Core.Shapes;

namespace PixelCreatures.Core
{
    /// <summary>
    /// 注册所有内置内容。
    /// <para>
    /// <c>static class</c> 表示这是一个"静态类"：不能实例化（不能 new），
    /// 所有成员都是静态的。Java 里对应"全是 static 方法的工具类"（如 java.lang.Math）。
    /// </para>
    /// </summary>
    public static class BuiltIns
    {
        /// <summary>
        /// 把所有内置部件注册到 <paramref name="r"/> 里。
        /// <para>
        /// <c>&lt;paramref name="r"/&gt;</c> 是 XML 文档注释的语法，用于在 IDE 里高亮参数名。
        /// 它不影响运行，纯粹是给人和 IDE 看的。
        /// </para>
        /// </summary>
        public static void RegisterAll(CreatureRegistry r)
        {
            // ---- 1. 配色规则：决定"基因值怎么变成具体颜色"
            // new DefaultPaletteRule() 是创建对象。C# 和 Java 一样用 new。
            r.RegisterPaletteRule(new DefaultPaletteRule());

            // ---- 2. 材质样式：毛皮/鳞片/石头/树皮等表面的渲染风格
            // foreach (var m in MaterialStyles.All) 遍历一个静态集合，
            // 相当于 Java 的 for (var m : MaterialStyles.All)
            foreach (var m in MaterialStyles.All) r.RegisterMaterial(m);

            // ---- 3. 形状块：可复用的身体部件（腿、头、翅膀、尾巴……）
            // 定义在 BuiltInBlocks 里，内部再逐个注册
            BuiltInBlocks.RegisterAll(r);

            // ---- 4. 运动模块：决定生物怎么动
            // 注意这里传的是 lambda：() => new LeggedLocomotion()
            // 意思是"需要时再创建"，而不是现在就创建。
            // 这叫"工厂委托"——注册表存的是"怎么造"，不是"造好的对象"。
            //
            // Java 对照：等价于 Supplier<ILocomotionModule>
            //     r.registerLocomotion("legged", LeggedLocomotion::new);
            r.RegisterLocomotion("legged", () => new LeggedLocomotion());        // 腿行：2~8 条腿
            r.RegisterLocomotion("flight", () => new FlightLocomotion());        // 飞行：鸟/蝙蝠/龙/虫
            r.RegisterLocomotion("swim", () => new SwimLocomotion());            // 游动：鱼/鲨/鳐
            r.RegisterLocomotion("serpentine", () => new SerpentineLocomotion()); // 蛇行：蛇/蠕虫
            r.RegisterLocomotion("hop", () => new HopLocomotion());              // 跳跃：史莱姆
            r.RegisterLocomotion("float", () => new FloatLocomotion());          // 漂浮：水母/鬼火

            // ---- 5. 体型家族：8 个内置的（植物族是扩展，单独注册）
            // 每个家族负责：基因表 + 采样 + 造骨架
            // 想看某个家族怎么实现，去 addons/procedural_creatures/Core/Families/
            r.RegisterFamily(new QuadrupedFamily());   // 四足兽类（9 种原型）
            r.RegisterFamily(new BipedFamily());       // 双足与类人（9 种原型，含装备）
            r.RegisterFamily(new ReptileFamily());     // 爬行动物与龙
            r.RegisterFamily(new ArthropodFamily());   // 节肢动物
            r.RegisterFamily(new WingedFamily());      // 有翼生物
            r.RegisterFamily(new SerpentFamily());     // 蛇与蠕虫
            r.RegisterFamily(new AquaticFamily());     // 水生生物
            r.RegisterFamily(new AmorphousFamily());   // 史莱姆与触手生物
        }
    }
}
