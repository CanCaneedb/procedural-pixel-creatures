// Procedural Pixel Creatures - small double precision linear algebra used by the core.
// Engine independent on purpose: the core must run on worker threads and in tools without Godot.
//
// ============================================================================
// 【给 Java 开发者的说明】
//
// 这个文件是理解本项目 C# 风格的起点，因为它展示了三个 Java 没有的特性：
//
//   1. struct（结构体）—— 值类型，赋值时复制整份数据，不像 Java 的 class 复制引用。
//   2. 运算符重载 —— 可以直接写 a + b，不用写 a.add(b)。
//   3. 属性（property）—— 写 `X { get; }`，调用时写 `v.X`（不带括号），
//      相当于 Java 里 `private final double x; public double getX() { return x; }`。
//
// 为什么用 struct 而不是 class？
//   向量是"值"而不是"有身份的对象"。用值类型：
//     - 不产生垃圾（没有 GC 压力）—— 本项目每帧要算几万次向量，这点很重要
//     - 赋值语义直观（副本独立，不会被别处偷偷改掉）
//   代价：struct 不能为 null（除非写 Vec2?），且不该太大（建议不超过 16~24 字节）。
// ============================================================================

using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace PixelCreatures.Core
{
    /// <summary>
    /// 二维向量（x, y），双精度。
    /// <para>
    /// Java 对照：<c>readonly struct</c> 相当于一个所有字段都是 final 的不可变值类型。
    /// 注意 <c>readonly</c> 修饰的是整个 struct（所有字段都不能改），
    /// 不是像 Java 那样逐个字段加 final。
    /// </para>
    /// <para>
    /// <c>: IEquatable&lt;Vec2&gt;</c> 是泛型接口实现，等价于 Java 的 <c>implements Comparable&lt;Vec2&gt;</c>
    /// 之类。实现了它，<c>List.Contains</c> 等操作就不会因为装箱而变慢。
    /// </para>
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        /// <summary>横坐标（向右为正）。</summary>
        /// <remarks>Java 对照：<c>public final double x;</c></remarks>
        public readonly double X;

        /// <summary>纵坐标（向上为正；但渲染到屏幕时要翻转，见 PixelCamera）。</summary>
        public readonly double Y;

        /// <summary>构造函数。C# 里构造函数名字和类名相同，没有 Java 那种默认无参构造。</summary>
        public Vec2(double x, double y) { X = x; Y = y; }

        // ---- 常用常量。static readonly = 只初始化一次，之后不可变（类似 Java 的 static final）
        public static readonly Vec2 Zero = new Vec2(0, 0);
        public static readonly Vec2 One = new Vec2(1, 1);
        /// <summary>单位向量 (1,0)，即"向右"。</summary>
        public static readonly Vec2 UnitX = new Vec2(1, 0);
        /// <summary>单位向量 (0,1)，即"向上"。</summary>
        public static readonly Vec2 UnitY = new Vec2(0, 1);

        // ---- 属性（property）：写法是「类型 名字 => 表达式」，调用方写 v.Length 而不是 v.Length()
        /// <summary>向量长度（欧几里得范数）。每次访问都重新计算，不是缓存字段。</summary>
        /// <remarks>Java 对照：<c>public double getLength() { return Math.sqrt(...); }</c></remarks>
        public double Length => Math.Sqrt(X * X + Y * Y);

        /// <summary>长度的平方。比较长度时用它可以省掉一次开方，更快。</summary>
        public double LengthSquared => X * X + Y * Y;

        /// <summary>
        /// 归一化：把向量缩放到长度为 1，保留方向。零向量无法归一化，返回 Zero。
        /// </summary>
        /// <remarks>
        /// 这里用 <c>1e-12</c> 而不是 0 做判断，是因为浮点数几乎不会精确等于 0，
        /// 除以一个极小的数会得到 Infinity 或 NaN，进而污染整个计算。
        /// </remarks>
        public Vec2 Normalized()
        {
            double l = Length;
            return l > 1e-12 ? new Vec2(X / l, Y / l) : Zero;
        }

        /// <summary>垂直向量：逆时针旋转 90°。用的是 2D 叉积的常见技巧。</summary>
        public Vec2 Perp => new Vec2(-Y, X);

        /// <summary>点积。几何意义：两向量夹角的余弦 × 各自长度。为 0 表示互相垂直。</summary>
        /// <remarks>
        /// <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c> 是 C# 特性（Attribute），
        /// 写法是方括号而不是 Java 的 @，作用是建议编译器把这个小方法内联展开，减少函数调用开销。
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>二维叉积（标量）。正负号表示 b 在 a 的左侧还是右侧，常用于判断转向。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

        /// <summary>
        /// 线性插值：t=0 返回 a，t=1 返回 b，t=0.5 返回中点。
        /// <para>这是本项目用得最多的数学函数之一，几乎所有"按基因值取范围"的地方都用它。</para>
        /// </summary>
        public static Vec2 Lerp(Vec2 a, Vec2 b, double t) => new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        /// <summary>两点距离。</summary>
        public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

        /// <summary>
        /// 由角度（弧度）构造单位向量。
        /// <para>
        /// 注意这里用的是项目自己的 <c>DMath.SinCos</c> 而不是 <c>System.Math</c>：
        /// 因为 <c>System.Math</c> 的三角函数在不同操作系统上最后一位可能有差异，
        /// 会导致同一种子在不同平台生成不同生物。详见 DMath.cs 的说明。
        /// </para>
        /// <para>
        /// <c>out double s</c> 是 C# 的 out 参数：方法通过它"返回"额外的值。
        /// Java 里通常得包一个对象或用数组，C# 直接写 out 更轻量。
        /// </para>
        /// </summary>
        public static Vec2 FromAngle(double radians) { DMath.SinCos(radians, out double s, out double c); return new Vec2(c, s); }

        /// <summary>该向量相对 +X 轴的角度（弧度）。</summary>
        public double Angle => DMath.Atan2(Y, X);

        /// <summary>绕原点旋转指定弧度（逆时针）。标准 2D 旋转矩阵。</summary>
        public Vec2 Rotated(double radians)
        {
            DMath.SinCos(radians, out double s, out double c);
            return new Vec2(X * c - Y * s, X * s + Y * c);
        }

        // ---- 运算符重载：Java 完全没有这个能力，这是 C# 最"好用"的特性之一。
        // 有了这些，代码里可以直接写 a + b、a * 2.0，而不必写 a.add(b)、a.scale(2.0)。
        // 每个运算符本质上仍是一个 static 方法，只是语法上允许用符号调用。
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        /// <summary>一元负号：取反方向。</summary>
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);
        public static Vec2 operator *(Vec2 a, double s) => new Vec2(a.X * s, a.Y * s);
        /// <summary>标量在左边的乘法（为了能写 2.0 * v）。交换律在类型层面不会自动成立，所以要写两份。</summary>
        public static Vec2 operator *(double s, Vec2 a) => new Vec2(a.X * s, a.Y * s);
        public static Vec2 operator /(Vec2 a, double s) => new Vec2(a.X / s, a.Y / s);

        /// <summary>是否为有限值（排除 NaN 和 Infinity）。用于防御上游计算出错。</summary>
        public bool IsFinite => DMath.IsFinite(X) && DMath.IsFinite(Y);

        /// <summary>
        /// 实现 <c>IEquatable&lt;Vec2&gt;</c> 要求的方法：按值比较。
        /// <para>
        /// 注意：这里用 <c>==</c> 比较 double，对浮点数通常不安全（0.1+0.2 != 0.3）。
        /// 本项目只在"精确复现"的场景用它（同一种子两次生成的值是逐位相同的）。
        /// </para>
        /// </summary>
        public bool Equals(Vec2 o) => X == o.X && Y == o.Y;

        /// <summary>
        /// 重写 <c>object.Equals</c>（对应 Java 的 <c>equals(Object)</c>）。
        /// <para>
        /// <c>obj is Vec2 v</c> 是 C# 的模式匹配：先判断类型，再同时把值赋给新变量 v。
        /// Java 16+ 的 <c>obj instanceof Vec2 v</c> 几乎一样。
        /// </para>
        /// <para>
        /// <c>object?</c> 里的问号表示"可能为 null"（可空引用类型），
        /// 这是 C# 8 引入的编译期检查，Java 没有等价物（Java 的注解靠工具检查）。
        /// </para>
        /// </summary>
        public override bool Equals(object? obj) => obj is Vec2 v && Equals(v);

        /// <summary>重写 GetHashCode（对应 Java 的 hashCode）。HashCode.Combine 是 C# 的内置工具。</summary>
        public override int GetHashCode() => HashCode.Combine(X, Y);

        /// <summary>
        /// 重写 ToString（对应 Java 的 toString）。
        /// <para>
        /// <c>CultureInfo.InvariantCulture</c> 指定用不变文化格式化数字，
        /// 否则在某些语言环境下小数点会变成逗号（例如德语 1,5 而不是 1.5），导致日志和测试不稳定。
        /// </para>
        /// </summary>
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###})", X, Y);
    }

    /// <summary>
    /// 三维向量（x, y, z），双精度。
    /// <para>
    /// 【本项目采用的坐标系】（很重要，看不懂方向时回来查这张表）
    /// <list type="bullet">
    /// <item>+X = 东（屏幕右）</item>
    /// <item>+Y = 上（世界高度）</item>
    /// <item>+Z = 南（朝向观察者）</item>
    /// </list>
    /// 相机从北向南俯视，仰角固定 30°（经典 RPG 的 3/4 视角）。
    /// 详见 PixelCamera.cs。
    /// </para>
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        /// <summary>东向分量。</summary>
        public readonly double X;
        /// <summary>高度分量。</summary>
        public readonly double Y;
        /// <summary>南向分量。</summary>
        public readonly double Z;

        public Vec3(double x, double y, double z) { X = x; Y = y; Z = z; }

        public static readonly Vec3 Zero = new Vec3(0, 0, 0);
        public static readonly Vec3 One = new Vec3(1, 1, 1);
        /// <summary>东。生物的"前方"通常从这个方向开始。</summary>
        public static readonly Vec3 UnitX = new Vec3(1, 0, 0);
        /// <summary>上。直立生物的脊柱方向。</summary>
        public static readonly Vec3 UnitY = new Vec3(0, 1, 0);
        /// <summary>南，即朝向观察者。</summary>
        public static readonly Vec3 UnitZ = new Vec3(0, 0, 1);

        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public double LengthSquared => X * X + Y * Y + Z * Z;

        /// <summary>归一化；零向量返回 Zero。</summary>
        public Vec3 Normalized()
        {
            double l = Length;
            return l > 1e-12 ? new Vec3(X / l, Y / l, Z / l) : Zero;
        }

        /// <summary>归一化；零向量返回调用方指定的 fallback。比 Normalized 更适合"必须有个方向"的场合。</summary>
        public Vec3 NormalizedOr(Vec3 fallback)
        {
            double l = Length;
            return l > 1e-12 ? new Vec3(X / l, Y / l, Z / l) : fallback;
        }

        // ---- 投影到二维（丢一个轴）。名字里的 XY/XZ 表示"保留哪两个轴"。
        /// <summary>投影到屏幕平面（丢掉高度）。</summary>
        public Vec2 XY => new Vec2(X, Y);
        /// <summary>投影到地面平面（丢掉高度），常用于水平距离判断。</summary>
        public Vec2 XZ => new Vec2(X, Z);
        /// <summary>只替换 Y，返回新向量（原向量不变，因为 struct 是值类型）。</summary>
        public Vec3 WithY(double y) => new Vec3(X, y, Z);
        public Vec3 WithX(double x) => new Vec3(x, Y, Z);
        public Vec3 WithZ(double z) => new Vec3(X, Y, z);

        /// <summary>点积。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Dot(Vec3 a, Vec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        /// <summary>叉积。结果垂直于 a 和 b，用于求法线、构造正交基。</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec3 Cross(Vec3 a, Vec3 b) => new Vec3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

        /// <summary>三维线性插值。用于姿态过渡、骨骼位置混合。</summary>
        public static Vec3 Lerp(Vec3 a, Vec3 b, double t) => new Vec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

        public static double Distance(Vec3 a, Vec3 b) => (a - b).Length;
        /// <summary>逐分量取较小值。</summary>
        public static Vec3 Min(Vec3 a, Vec3 b) => new Vec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
        /// <summary>逐分量取较大值。</summary>
        public static Vec3 Max(Vec3 a, Vec3 b) => new Vec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));
        /// <summary>逐分量相乘（不是点积）。用于按轴分别缩放。</summary>
        public static Vec3 Scale(Vec3 a, Vec3 b) => new Vec3(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        /// <summary>
        /// 二次贝塞尔曲线求值。用于尾巴、颈部等弯曲链的平滑插值。
        /// <para>公式：B(t) = a(1-t)² + b·2(1-t)t + c·t²</para>
        /// </summary>
        public static Vec3 Bezier(Vec3 a, Vec3 b, Vec3 c, double t)
        {
            double u = 1 - t;
            return a * (u * u) + b * (2 * u * t) + c * (t * t);
        }

        // ---- 运算符重载（同 Vec2，让向量运算写起来像数学公式）
        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 a) => new Vec3(-a.X, -a.Y, -a.Z);
        public static Vec3 operator *(Vec3 a, double s) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator *(double s, Vec3 a) => new Vec3(a.X * s, a.Y * s, a.Z * s);
        public static Vec3 operator /(Vec3 a, double s) => new Vec3(a.X / s, a.Y / s, a.Z / s);

        public bool IsFinite => DMath.IsFinite(X) && DMath.IsFinite(Y) && DMath.IsFinite(Z);
        public bool Equals(Vec3 o) => X == o.X && Y == o.Y && Z == o.Z;
        public override bool Equals(object? obj) => obj is Vec3 v && Equals(v);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", X, Y, Z);
    }

    /// <summary>Unit quaternion rotation (x, y, z, w).</summary>
    /// <summary>
    /// 单位四元数（x, y, z, w），用来表示三维旋转。
    /// <para>
    /// 【为什么不用欧拉角或矩阵？】
    /// <list type="bullet">
    /// <item>欧拉角有"万向锁"问题，且插值不自然。</item>
    /// <item>矩阵占用 9 个 double，而且反复相乘会累积误差、失去正交性。</item>
    /// <item>四元数只占 4 个数，插值平滑（Nlerp），适合骨骼动画混合。</item>
    /// </list>
    /// 骨骼的旋转全部用四元数存储，见 BoneDef.RestRotation。
    /// </para>
    /// <para>
    /// 【直觉理解】可以把 (x,y,z) 看成旋转轴方向乘以 sin(θ/2)，w 看成 cos(θ/2)。
    /// 所以 w 接近 1 表示"几乎没旋转"。
    /// </para>
    /// </summary>
    public readonly struct Quat : IEquatable<Quat>
    {
        /// <summary>旋转轴 X 分量（已乘 sin(θ/2)）。</summary>
        public readonly double X;
        /// <summary>旋转轴 Y 分量。</summary>
        public readonly double Y;
        /// <summary>旋转轴 Z 分量。</summary>
        public readonly double Z;
        /// <summary>标量部分 = cos(θ/2)。</summary>
        public readonly double W;

        public Quat(double x, double y, double z, double w) { X = x; Y = y; Z = z; W = w; }

        /// <summary>单位四元数：表示"不旋转"。所有旋转计算的起点。</summary>
        public static readonly Quat Identity = new Quat(0, 0, 0, 1);

        /// <summary>
        /// 绕指定轴旋转指定角度（弧度）。
        /// <para>注意角度要除以 2：这是四元数表示旋转的定义决定的（half-angle）。</para>
        /// </summary>
        public static Quat AxisAngle(Vec3 axis, double angle)
        {
            Vec3 n = axis.Normalized();
            DMath.SinCos(angle * 0.5, out double s, out double c);
            return new Quat(n.X * s, n.Y * s, n.Z * s, c);
        }

        /// <summary>绕 X 轴旋转（俯仰方向，在本项目坐标系里是"抬头/低头"的侧倾轴之一）。</summary>
        public static Quat RotX(double a) { DMath.SinCos(a * 0.5, out double s, out double c); return new Quat(s, 0, 0, c); }
        /// <summary>绕 Y 轴旋转（偏航，即左右转向）。项目里生物转身主要用这个。</summary>
        public static Quat RotY(double a) { DMath.SinCos(a * 0.5, out double s, out double c); return new Quat(0, s, 0, c); }
        /// <summary>绕 Z 轴旋转（翻滚）。</summary>
        public static Quat RotZ(double a) { DMath.SinCos(a * 0.5, out double s, out double c); return new Quat(0, 0, s, c); }

        /// <summary>
        /// 由欧拉角组合出旋转：先偏航(Y)，再俯仰(Z)，最后翻滚(X)。
        /// <para>
        /// 【顺序很重要】四元数乘法不满足交换律，a*b 不等于 b*a。
        /// 这里的顺序是按本项目生物坐标系（+X 前、+Y 上、+Z 右）约定的。
        /// </para>
        /// </summary>
        public static Quat Euler(double yaw, double pitch, double roll) => RotY(yaw) * RotZ(pitch) * RotX(roll);

        /// <summary>归一化。反复相乘后会漂移，归一化把长度拉回 1。</summary>
        public Quat Normalized()
        {
            double l = Math.Sqrt(X * X + Y * Y + Z * Z + W * W);
            return l > 1e-12 ? new Quat(X / l, Y / l, Z / l, W / l) : Identity;
        }

        /// <summary>共轭四元数 = 反向旋转。用它可以把"变换到某坐标系"再"变换回来"。</summary>
        public Quat Conjugate => new Quat(-X, -Y, -Z, W);

        /// <summary>
        /// 四元数乘法 = 复合旋转：先应用 b，再应用 a（注意顺序，和矩阵乘法一致）。
        /// <para>这串公式是四元数乘法的标准定义，不需要背，知道语义即可。</para>
        /// </summary>
        public static Quat operator *(Quat a, Quat b) => new Quat(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

        /// <summary>
        /// 用四元数旋转一个向量。这是骨骼动画里每帧要调用几十次的核心操作。
        /// <para>
        /// 用的是"三次叉积"公式：v' = v + 2w(q×v) + 2q×(q×v)，
        /// 比转成 3x3 矩阵再相乘更快（省去矩阵构造）。
        /// </para>
        /// </summary>
        public Vec3 Rotate(Vec3 v)
        {
            // v' = v + 2w(q x v) + 2 q x (q x v)
            Vec3 q = new Vec3(X, Y, Z);
            Vec3 t = Vec3.Cross(q, v) * 2.0;
            return v + t * W + Vec3.Cross(q, t);
        }

        public static double Dot(Quat a, Quat b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

        /// <summary>
        /// 归一化线性插值（Nlerp），沿最短弧。
        /// <para>
        /// 【为什么不用 slerp？】slerp 数学上更"均匀"，但要算三角函数，慢。
        /// 骨骼动画里角度差通常很小，Nlerp 的视觉差异可以忽略，而速度快很多。
        /// </para>
        /// <para>
        /// 开头的 <c>if (Dot(a,b) &lt; 0)</c> 是关键：四元数 q 和 -q 表示同一个旋转，
        /// 如果点积为负说明两者"方向相反"，直接插值会绕远路（转一大圈）。
        /// 把 b 取反后就变成走短弧。
        /// </para>
        /// </summary>
        public static Quat Nlerp(Quat a, Quat b, double t)
        {
            if (Dot(a, b) < 0) b = new Quat(-b.X, -b.Y, -b.Z, -b.W);
            return new Quat(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t).Normalized();
        }

        /// <summary>
        /// 求"把方向 a 转到方向 b"的最短旋转。
        /// <para>用于让骨骼自动对齐到目标方向（例如让腿指向落点）。</para>
        /// <para>
        /// 三种情况：几乎同向直接返回 Identity；几乎反向需要任选一个垂直轴转 180°；
        /// 一般情况用叉积构造。
        /// </para>
        /// </summary>
        public static Quat FromTo(Vec3 a, Vec3 b)
        {
            Vec3 na = a.Normalized(), nb = b.Normalized();
            double d = Vec3.Dot(na, nb);
            if (d > 0.999999) return Identity;
            if (d < -0.999999)
            {
                Vec3 axis = Vec3.Cross(Vec3.UnitX, na);
                if (axis.LengthSquared < 1e-8) axis = Vec3.Cross(Vec3.UnitY, na);
                return AxisAngle(axis, DMath.Pi);
            }
            Vec3 c = Vec3.Cross(na, nb);
            return new Quat(c.X, c.Y, c.Z, 1.0 + d).Normalized();
        }

        public bool IsFinite => DMath.IsFinite(X) && DMath.IsFinite(Y) && DMath.IsFinite(Z) && DMath.IsFinite(W);
        public bool Equals(Quat o) => X == o.X && Y == o.Y && Z == o.Z && W == o.W;
        public override bool Equals(object? obj) => obj is Quat q && Equals(q);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    }

    /// <summary>
    /// 3x3 矩阵，按"三个列向量"存储（C0/C1/C2 是三个基轴）。
    /// <para>
    /// 【为什么存列而不是按行列存 9 个数？】
    /// 因为矩阵在这里主要表示"坐标系"：三个列向量就是该坐标系的 X/Y/Z 轴在世界里的方向。
    /// 这样读代码时能直接看出每个轴指向哪里，比一堆 m00..m22 直观得多。
    /// </para>
    /// <para>用于把世界坐标变换到相机视角（见 PixelCamera.WorldToView）。</para>
    /// </summary>
    public readonly struct Mat3
    {
        public readonly Vec3 C0, C1, C2;

        public Mat3(Vec3 c0, Vec3 c1, Vec3 c2) { C0 = c0; C1 = c1; C2 = c2; }

        public static readonly Mat3 Identity = new Mat3(Vec3.UnitX, Vec3.UnitY, Vec3.UnitZ);

        public static Mat3 FromQuat(Quat q)
        {
            double xx = q.X * q.X, yy = q.Y * q.Y, zz = q.Z * q.Z;
            double xy = q.X * q.Y, xz = q.X * q.Z, yz = q.Y * q.Z;
            double wx = q.W * q.X, wy = q.W * q.Y, wz = q.W * q.Z;
            return new Mat3(
                new Vec3(1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy)),
                new Vec3(2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx)),
                new Vec3(2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy)));
        }

        public static Mat3 Diagonal(double a, double b, double c) => new Mat3(new Vec3(a, 0, 0), new Vec3(0, b, 0), new Vec3(0, 0, c));

        /// <summary>Orthonormal basis whose X axis points along forward and whose Y axis is as close as possible to up.</summary>
        public static Mat3 LookAlong(Vec3 forward, Vec3 up)
        {
            Vec3 x = forward.NormalizedOr(Vec3.UnitX);
            Vec3 z = Vec3.Cross(x, up);
            if (z.LengthSquared < 1e-10) z = Vec3.Cross(x, Math.Abs(x.Y) < 0.9 ? Vec3.UnitY : Vec3.UnitX);
            z = z.Normalized();
            Vec3 y = Vec3.Cross(z, x).Normalized();
            return new Mat3(x, y, z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 Mul(Vec3 v) => C0 * v.X + C1 * v.Y + C2 * v.Z;

        /// <summary>Transpose(M) * v. For rotations this is the inverse transform.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 MulTransposed(Vec3 v) => new Vec3(Vec3.Dot(C0, v), Vec3.Dot(C1, v), Vec3.Dot(C2, v));

        public static Mat3 operator *(Mat3 a, Mat3 b) => new Mat3(a.Mul(b.C0), a.Mul(b.C1), a.Mul(b.C2));

        public Mat3 Transposed => new Mat3(
            new Vec3(C0.X, C1.X, C2.X),
            new Vec3(C0.Y, C1.Y, C2.Y),
            new Vec3(C0.Z, C1.Z, C2.Z));

        public Mat3 ScaledColumns(double sx, double sy, double sz) => new Mat3(C0 * sx, C1 * sy, C2 * sz);

        public double Determinant => Vec3.Dot(C0, Vec3.Cross(C1, C2));

        public Mat3 Inverse()
        {
            Vec3 r0 = Vec3.Cross(C1, C2);
            Vec3 r1 = Vec3.Cross(C2, C0);
            Vec3 r2 = Vec3.Cross(C0, C1);
            double det = Vec3.Dot(C0, r0);
            if (Math.Abs(det) < 1e-18) return Identity;
            double inv = 1.0 / det;
            // rows of the inverse are r0, r1, r2 scaled
            return new Mat3(
                new Vec3(r0.X * inv, r1.X * inv, r2.X * inv),
                new Vec3(r0.Y * inv, r1.Y * inv, r2.Y * inv),
                new Vec3(r0.Z * inv, r1.Z * inv, r2.Z * inv));
        }

        /// <summary>Re-orthonormalizes a rotation basis (Gram-Schmidt, X axis dominant).</summary>
        public Mat3 Orthonormalized()
        {
            Vec3 x = C0.NormalizedOr(Vec3.UnitX);
            Vec3 y = (C1 - x * Vec3.Dot(x, C1)).NormalizedOr(Vec3.UnitY);
            Vec3 z = Vec3.Cross(x, y);
            return new Mat3(x, y, z);
        }
    }

    /// <summary>Rigid transform: rotation basis and translation.</summary>
    public readonly struct Xform
    {
        public readonly Mat3 Basis;
        public readonly Vec3 Origin;

        public Xform(Mat3 basis, Vec3 origin) { Basis = basis; Origin = origin; }

        public static readonly Xform Identity = new Xform(Mat3.Identity, Vec3.Zero);

        public static Xform FromQuat(Quat q, Vec3 origin) => new Xform(Mat3.FromQuat(q), origin);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 Point(Vec3 local) => Basis.Mul(local) + Origin;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 Dir(Vec3 local) => Basis.Mul(local);

        /// <summary>Inverse transform for rigid (orthonormal) bases.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 InversePoint(Vec3 world) => Basis.MulTransposed(world - Origin);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec3 InverseDir(Vec3 world) => Basis.MulTransposed(world);

        public static Xform operator *(Xform parent, Xform local) => new Xform(parent.Basis * local.Basis, parent.Point(local.Origin));

        public Vec3 AxisX => Basis.C0;
        public Vec3 AxisY => Basis.C1;
        public Vec3 AxisZ => Basis.C2;
    }
}
