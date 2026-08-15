# Harmony 2.x 使用示例 Demo

> 演示如何使用 [Lib.Harmony](https://github.com/pardeike/Harmony)（NuGet）在运行时拦截和修改 .NET 方法。

## 依赖说明

| 包 | 说明 |
|---|---|
| `Lib.Harmony 2.4.2` | **推荐方式** — 第三方依赖（Mono.Cecil 等）已合并打包，开箱即用，无需额外引入任何依赖 |
| `Lib.Harmony.Thin` | 另一种选择 — 不合并依赖，需要自行引入 Mono.Cecil，更小但需手动管理 |

**支持的框架**：.NET 5.0 / .NET Core 3.0 / .NET Standard 2.0 / .NET Framework 3.5+

## 编译运行

```bash
cd HarmonyDemo
dotnet restore   # 恢复 NuGet 包（Lib.Harmony 自动下载）
dotnet build     # 编译
dotnet run       # 运行演示
```

## 演示内容

| 示例 | 演示内容 | 关键 API |
|------|---------|---------|
| 示例 1 | `Prefix` + `Postfix`：拦截参数、截断返回值 | `harmony.Patch(..., prefix:, postfix:)` |
| 示例 2 | `Postfix`：修改字符串返回值 | `ref string __result` |
| 示例 3 | `Declarative` 声明式写法：参数+返回值双拦截 | `[HarmonyPatch]` + `CreateClassProcessor` |
| 示例 4 | `Finalizer`：捕获异常，提供默认值 | `Exception? __exception` |
| 示例 5 | `UnpatchAll`：按 ID 卸载所有 Patch | `harmony.UnpatchAll(id)` |

## 核心概念

```
调用 TargetClass.Add(5, 7)

执行顺序：
  Prefix → (原方法) → Postfix → Finalizer

若 Prefix 返回 false：跳过原方法，直接执行后续 Patch
若原方法抛异常：Prefix/Postfix 不执行，Finalizer 仍会执行

特殊参数：
  __result  — 接收或修改原方法的返回值（Postfix/Finalizer 中使用 ref）
  __instance — 接收 this 实例（实例方法）
  __exception — 捕获原方法的异常（Finalizer）
  __state    — 在 Prefix 和 Postfix 之间传递自定义状态
```

## 关键 API 速查

```csharp
// 1. 初始化
var harmony = new Harmony("com.example.myplugin");

// 2. 获取方法
var method = AccessTools.Method(typeof(MyClass), "MethodName");
var method = AccessTools.Method(typeof(MyClass), "MethodName", new[] { typeof(int) });

// 3. 基础 Patch
harmony.Patch(method,
    prefix:   new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.MyPrefix)),
    postfix:  new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.MyPostfix)),
    finalizer:new HarmonyMethod(typeof(MyPatch), nameof(MyPatch.MyFinalizer))
);

// 4. 卸载
harmony.UnpatchAll("com.example.myplugin");

// 5. 查询
harmony.GetPatchedMethods();       // 获取所有被 Patch 的方法
harmony.GetPatchInfo(method);      // 获取某个方法的 Patch 详情
```

## 声明式写法（推荐）

```csharp
[HarmonyPatch(typeof(TargetClass))]
[HarmonyPatch(nameof(TargetClass.MethodName))]
public static class MyDeclarativePatch
{
    public static bool Prefix(...) { ... return true; }
    public static void Postfix(...) { ... }
}

// 自动注册所有 [HarmonyPatch] 声明
var processor = Harmony.CreateClassProcessor(typeof(MyDeclarativePatch));
processor.Patch();
```
