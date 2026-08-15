using HarmonyLib;

// =============================================================================
// Harmony 2.x 完整使用示例
// 
// 依赖说明：
//   Lib.Harmony (NuGet) — 第三方依赖（Mono.Cecil 等）已合并打包，
//   无需手动引入任何其他 NuGet 包，开箱即用。
//
// 核心概念：
//   Harmony 通过运行时 IL 编织（IL Weaving）实现方法拦截，
//   不修改原始程序集文件，而是在运行时动态修改方法字节码。
//
//   三种 Patch 类型：
//     Prefix   — 在原方法执行之前运行，可修改参数 (ref params)
//     Postfix  — 在原方法执行之后运行，可修改返回值 (ref result)
//     Finalizer— 在原方法执行完成后运行（即使原方法抛异常），用于异常捕获
//
//   Transpiler — 在 IL 层面修改方法的字节码，用于批量修改变量/注入逻辑
//
// =============================================================================

namespace HarmonyDemo;


// =============================================================================
// 一、演示用的目标类（要被拦截的原始代码）
// =============================================================================

public static class TargetClass
{
    public static string LastCallResult = "";

    public static int Add(int a, int b)
    {
        int result = a + b;
        Console.WriteLine($"  [原始方法] Add({a}, {b}) = {result}");
        return result;
    }

    public static string GetMessage(string name)
    {
        LastCallResult = $"Hello, {name}!";
        Console.WriteLine($"  [原始方法] GetMessage(\"{name}\") = \"{LastCallResult}\"");
        return LastCallResult;
    }

    public static void DivideByZero()
    {
        Console.WriteLine("  [原始方法] DivideByZero 即将触发异常...");
        int x = 10 / int.Parse("0"); // 抛 ArgumentException
    }
}


// =============================================================================
// 二、Patch 类：演示各种拦截方式
// =============================================================================

/// <summary>
/// Prefix — 在原方法执行「之前」运行
/// 
/// 技术要点：
///   - [HarmonyPrefix] 标记此方法为前缀补丁
///   - 第一个参数自动接收原方法的参数（按顺序）
///   - 如果返回 false，原方法「不会执行」
///   - ref/out 参数同样支持
/// </summary>
public static class AddPrefixPatch
{
    public static bool Prefix(int a, int b)
    {
        Console.WriteLine($"  [Prefix] 即将执行 Add({a}, {b})");
        Console.WriteLine($"  [Prefix] a > 0 ? {a > 0}, b > 0 ? {b > 0}");
        return true; // 继续执行原方法（return false 会跳过原方法）
    }

    /// <summary>
    /// Postfix — 在原方法执行「之后」运行
    /// 
    /// 技术要点：
    ///   - [HarmonyPostfix] 标记
    ///   - 用 ref 修饰参数名即可「获取」或「修改」返回值
    ///   - 如果原方法返回 void，用 ref __result 捕获（若有的话）
    /// </summary>
    public static void Postfix(ref int __result)
    {
        Console.WriteLine($"  [Postfix] 原方法返回值: {__result}");
        if (__result > 100)
        {
            Console.WriteLine($"  [Postfix] 结果超过100，按100截断");
            __result = 100;
        }
    }
}


/// <summary>
/// 演示 Postfix — 修改返回值
/// </summary>
public static class MessagePostfixPatch
{
    public static void Postfix(ref string __result)
    {
        Console.WriteLine($"  [Postfix] 原始返回: \"{__result}\"");
        __result = "[拦截后] " + __result + " [Patched!]";
        Console.WriteLine($"  [Postfix] 修改后返回: \"{__result}\"");
    }
}


/// <summary>
/// Finalizer — 在方法执行完成后运行（无论是否抛异常）
/// 
/// 技术要点：
///   - [HarmonyFinalizer] 标记
///   - 可捕获原方法的异常（参数名 __exception）
///   - 如果原方法有返回值，用 ref __result 捕获
///   - 执行顺序在 Postfix 之后
/// </summary>
public static class DivideFinalizerPatch
{
    public static void Finalizer(Exception? __exception, ref int __result)
    {
        if (__exception != null)
        {
            Console.WriteLine($"  [Finalizer] 捕获到异常: {__exception.GetType().Name}");
            Console.WriteLine($"  [Finalizer] 异常消息: {__exception.Message}");
            Console.WriteLine($"  [Finalizer] 将 __result 设为 -1 作为默认值");
            __result = -1;
        }
        else
        {
            Console.WriteLine($"  [Finalizer] 方法正常结束，返回值 = {__result}");
        }
    }
}


// =============================================================================
// 三、Declarative 声明式写法
//
//  写法对比：
//    方法一：独立 Patch 类 + harmony.Patch() + HarmonyMethod
//    方法二：[HarmonyPatch] 属性 + 嵌套子类声明方法（更简洁）
// =============================================================================

/// <summary>
/// 方法二：声明式写法
/// 直接在目标类中声明要 Patch 的方法，不需要独立 Patch 类
/// </summary>
[HarmonyPatch(typeof(TargetClass))]
public static class DeclarativePatch
{
    // 声明要 Patch 的方法：TargetClass.GetMessage
    [HarmonyPatch(nameof(TargetClass.GetMessage))]
    [HarmonyPriority(Priority.High)]
    public static class GetMessage
    {
        // Prefix：修改传入的参数
        public static bool Prefix(ref string name)
        {
            Console.WriteLine($"  [DeclarativePrefix] 原始 name = \"{name}\"");
            name = name.ToUpperInvariant();
            Console.WriteLine($"  [DeclarativePrefix] 修改后 name = \"{name}\"");
            return true; // 继续执行原方法
        }

        // Postfix：修改返回值
        public static void Postfix(ref string __result)
        {
            Console.WriteLine($"  [DeclarativePostfix] 原始返回值: \"{__result}\"");
            __result = __result.Replace("HELLO", "你好");
            Console.WriteLine($"  [DeclarativePostfix] 最终返回值: \"{__result}\"");
        }
    }
}


// =============================================================================
// 四、主程序入口
// =============================================================================

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("Harmony 2.x Runtime Patching 演示");
        Console.WriteLine("  Lib.Harmony NuGet — 第三方依赖已合并，无需额外引入");
        Console.WriteLine("================================================================================");
        Console.WriteLine();

        // ================================================================
        // 初始化：创建 Harmony 实例
        //   参数：唯一标识当前 Patch 集的 ID
        //   多个插件应使用不同的 ID，避免 Patch 冲突
        // ================================================================
        var harmony = new Harmony("com.harmonydemo.example");

        // ================================================================
        // 示例 1：拦截 Add — Prefix + Postfix
        //   Prefix:  在原方法执行前打印参数
        //   Postfix: 在原方法执行后，如果返回值超过100则截断为100
        // ================================================================
        Console.WriteLine("【示例 1】拦截 TargetClass.Add — Prefix + Postfix");
        Console.WriteLine("------------------------------------------------------------");

        var addMethod = AccessTools.Method(typeof(TargetClass), nameof(TargetClass.Add))
            ?? throw new InvalidOperationException("未找到 Add 方法");

        harmony.Patch(
            addMethod,
            prefix: new HarmonyMethod(typeof(AddPrefixPatch), nameof(AddPrefixPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(AddPrefixPatch), nameof(AddPrefixPatch.Postfix))
        );

        int addResult1 = TargetClass.Add(5, 7);    // Prefix 打印，Postfix 不截断（12 <= 100）
        int addResult2 = TargetClass.Add(50, 60);   // Prefix 打印，Postfix 截断（110 > 100 → 100）
        Console.WriteLine($"  最终 addResult1 = {addResult1}");
        Console.WriteLine($"  最终 addResult2 = {addResult2}  (被 Postfix 截断)");
        Console.WriteLine();

        // ================================================================
        // 示例 2：拦截 GetMessage — Postfix 修改返回值
        // ================================================================
        Console.WriteLine("【示例 2】拦截 TargetClass.GetMessage — Postfix 修改返回值");
        Console.WriteLine("------------------------------------------------------------");

        var getMsgMethod = AccessTools.Method(typeof(TargetClass), nameof(TargetClass.GetMessage))
            ?? throw new InvalidOperationException("未找到 GetMessage 方法");

        harmony.Patch(
            getMsgMethod,
            postfix: new HarmonyMethod(typeof(MessagePostfixPatch), nameof(MessagePostfixPatch.Postfix))
        );

        string msg = TargetClass.GetMessage("World");
        Console.WriteLine($"  最终返回: \"{msg}\"");
        Console.WriteLine();

        // ================================================================
        // 示例 3：Declarative 声明式 Patch — 参数 + 返回值同时拦截
        //   GetMessage 已有 MessagePostfixPatch 的 Postfix
        //   再加上 DeclarativePatch 的 Prefix 修改参数
        // ================================================================
        Console.WriteLine("【示例 3】Declarative 声明式 Patch — 参数 + 返回值同时拦截");
        Console.WriteLine("------------------------------------------------------------");
        Console.WriteLine("  GetMessage 已挂载：");
        Console.WriteLine("    - MessagePostfixPatch.Postfix：给返回值加前缀");
        Console.WriteLine("    - DeclarativePatch.GetMessage.Prefix：将 name 参数转大写");
        Console.WriteLine("    - DeclarativePatch.GetMessage.Postfix：将 HELLO 替换为中文");

        // CreateClassProcessor 是实例方法，在 harmony 实例上调用
        var processor = harmony.CreateClassProcessor(typeof(DeclarativePatch));
        processor.Patch();

        string msg2 = TargetClass.GetMessage("alice");
        Console.WriteLine($"  最终返回: \"{msg2}\"");
        Console.WriteLine();

        // ================================================================
        // 示例 4：Finalizer 捕获异常
        // ================================================================
        Console.WriteLine("【示例 4】拦截 DivideByZero — Finalizer 捕获异常");
        Console.WriteLine("------------------------------------------------------------");

        var divideMethod = AccessTools.Method(typeof(TargetClass), nameof(TargetClass.DivideByZero))
            ?? throw new InvalidOperationException("未找到 DivideByZero 方法");

        harmony.Patch(
            divideMethod,
            finalizer: new HarmonyMethod(typeof(DivideFinalizerPatch), nameof(DivideFinalizerPatch.Finalizer))
        );

        Console.WriteLine("  调用 DivideByZero（会抛异常）：");
        TargetClass.DivideByZero();
        Console.WriteLine();

        // ================================================================
        // 查看已注册的 Patch 信息
        // ================================================================
        Console.WriteLine("【Patch 信息查询】");
        Console.WriteLine("------------------------------------------------------------");
        var allPatched = harmony.GetPatchedMethods().ToList();
        Console.WriteLine($"  当前 Harmony 实例共 Patch 了 {allPatched.Count} 个方法：");
        foreach (var m in allPatched)
        {
            // GetPatchInfo 是静态方法
            var patches = Harmony.GetPatchInfo(m);
            Console.WriteLine($"  - {m.DeclaringType?.Name}.{m.Name}");
            if (patches != null)
            {
                // Prefixes/Postfixes/Finalizers 是 IEnumerable<Patch>
                // Patch 类属性：owner(string), priority(int), PatchMethod(MethodInfo), index(int)
                foreach (var p in patches.Prefixes)
                    Console.WriteLine($"      Prefix:   [{p.owner}] {p.PatchMethod?.Name}");
                foreach (var p in patches.Postfixes)
                    Console.WriteLine($"      Postfix:  [{p.owner}] {p.PatchMethod?.Name}");
                foreach (var p in patches.Finalizers)
                    Console.WriteLine($"      Finalizer:[{p.owner}] {p.PatchMethod?.Name}");
            }
        }
        Console.WriteLine();

        // ================================================================
        // 卸载 Patch（Unpatch）
        // ================================================================
        Console.WriteLine("【Unpatch 卸载】");
        Console.WriteLine("------------------------------------------------------------");

        harmony.UnpatchAll("com.harmonydemo.example");
        Console.WriteLine("  已卸载 ID = 'com.harmonydemo.example' 的所有 Patch");

        // 卸载后恢复原始行为
        string msgAfterUnpatch = TargetClass.GetMessage("AfterUnpatch");
        Console.WriteLine($"  卸载后 GetMessage(\"AfterUnpatch\") = \"{msgAfterUnpatch}\"");
        Console.WriteLine();

        Console.WriteLine("================================================================================");
        Console.WriteLine("演示完成！");
        Console.WriteLine("  - Prefix   : 原方法执行前运行，可修改参数，return false 跳过原方法");
        Console.WriteLine("  - Postfix  : 原方法执行后运行，可修改返回值（ref __result）");
        Console.WriteLine("  - Finalizer: 原方法执行后运行（无论是否抛异常），可捕获异常（__exception）");
        Console.WriteLine("  - Declarative: [HarmonyPatch] 属性声明，无需手动 Patch()");
        Console.WriteLine("  - UnpatchAll : 按 ID 卸载所有已注册的 Patch");
        Console.WriteLine("================================================================================");
    }
}
