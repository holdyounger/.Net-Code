# FunctionPointerDemo

验证 `RuntimeMethodHandle.GetFunctionPointer()` 的返回语义，以及**如何从 precode(stub) 解析出真实 native code**。

支持两种运行时 / 架构：

| 目标框架 | 运行时 | 架构 | 解析方式 |
|---|---|---|---|
| `net9.0` | CoreCLR (.NET 9) | x64（默认） | rip-relative 解码 precode 数据页 |
| `net48` | .NET Framework 4.x | x86 / x64（AnyCPU） | 特征码匹配 + 偏移解码（与 CoreCLR 同构）|

## 背景

`GetFunctionPointer()` 内部走 `MethodDesc::GetMultiCallableAddrOfCode`（`src/coreclr/vm/runtimehandles.cpp`）。它**不保证**返回真实 JIT 代码，在很多情况下返回的是 **precode（stub）**：

- **FixupPrecode**（NGEN/R2R 常见）：`jmp [data.Target]` + `mov r10,[data.MethodDesc]` + `jmp [data.PrecodeFixupThunk]`
- **StubPrecode**（普通 JIT）：`mov r10,[data.SecretParam]` + `jmp [data.Target]`

要拿到**真实 native code**，需要**跟随 precode 的 Target 槽位**（即"手动取偏移"），而不是直接拿 entry。

> CoreCLR 与 .NET Framework 的 precode 布局不同，解析方式也不同，见下文。

## 运行

### CoreCLR (.NET 9 / net9.0)

```bash
dotnet run -c Debug
```

### .NET Framework (net48)

构建（默认 AnyCPU，在 64 位 OS 上按 x64 进程运行）：

```bash
dotnet build -c Debug -f net48
bin\Debug\net48\FunctionPointerDemo.exe
```

构建并运行 **x86 (32 位)** 版本：

```bash
dotnet build -c Debug -f net48 -p:PlatformTarget=x86
```

> **注意**：x86 版本请在 **Windows 侧**双击 `run-net48-x86.bat` 运行，不要从 WSL2 直接执行——WSL2 interop 会把 32 位 exe 强制拉成 64 位宿主进程，导致 `Environment.Is64BitProcess=True`，无法验证真正的 32 位路径。

## 输出解读

每类方法会打印：

- `entry` —— `GetFunctionPointer()` 的原始返回值
- `isPrecode / precodeType` —— 是否为 precode 及其类型
- `methodDesc` —— 该方法的 MethodDesc 地址（**直接用 `mi.MethodHandle.Value` 获取**，不依赖 precode 解码）
- `realCode` —— 跟随 precode 后得到的**真实 native 代码**
- `entry==realCode` —— 若为 True，说明 entry 就是真实代码（无 precode）
- 各类字节 dump

### CoreCLR (.NET 9) 实测

.NET 9.0.17 x64 中，**普通/静态/虚方法均返回 FixupPrecode**，`realCode` 是真正的 JIT 机器码（以 `55 57 48 83 EC 28` = `push rbp; push rdi; sub rsp,28h` 开头）；P/Invoke 返回 StubPrecode，`realCode` 是 NDirect IL stub（不是 Win32 目标函数）。

### .NET Framework (net48) 实测

- **x64**：`PrepareMethod` 后 `GetFunctionPointer` 直接返回真实 JIT 代码（`55 48 83 EC 20`，无 precode），解析器匹配不到 precode 特征 → 原样返回 entry。
- **x86**：若 entry 命中 `A1 <SecretParam槽> FF 25 <Target槽>`（StubPrecode）或 `FF 25 <Target槽> A1 <MD槽> FF 25 <Thunk槽>`（FixupPrecode），按特征码解码 MethodDesc 与真实代码。

> **MethodDesc 获取方式**：解码方式在 x64 的 E9 跳板/已 JIT 代码下拿不到 MD（跳板里不内嵌 MD）。因此统一改用 **`mi.MethodHandle.Value`** —— 它直接返回该方法的 MethodDesc 指针（CLR 内部句柄，net48 / net9.0 语义一致），不经过 precode 解码，任何 entry 形态都能拿到。

## 解析原理

`Program.cs` 按运行时选择解析器：

- `ResolveCoreCLRNativeCode`（net9.0 / CoreCLR）：
  1. 读 entry 前几个字节做**特征码匹配**，判断是哪种 precode：
     - FixupPrecode：`FF 25 <disp32>  4C 8B 15 <disp32>  FF 25 <disp32>`
     - StubPrecode：`4C 8B 15 <disp32>  FF 25 <disp32>`
  2. **从指令里解码 rip 相对偏移**算出数据页地址：
     - `jmp [rip+disp32]` → `data = entry + 6 + disp32`
     - `mov r10,[rip+disp32]` → `data = entry + 7 + disp32`
     （不依赖硬编码页偏移，任何页大小都正确）
  3. 读数据页：
     - FixupPrecodeData：`{ Target(+0); MethodDesc(+8); PrecodeFixupThunk(+16) }`
     - StubPrecodeData：`{ SecretParam(+0); Target(+8); Type(+16) }`
  4. `Target` 即真实 native code；若 `Target` 仍指向另一个 precode，继续跟随（最多 8 跳）。

- `ResolveFrameworkNativeCode`（net48 / .NET Framework）：与 CoreCLR 同构的**特征码匹配 + 偏移解码**，不依赖 MethodDesc 内部布局；x64 直接复用 `ResolveOneStepCoreCLR`（另识别 E9/E8 纯跳板），x86 匹配 `A1 <SecretParam槽> FF 25 <Target槽>`（StubPrecode）与 `FF 25 <Target槽> A1 <MD槽> FF 25 <Thunk槽>`（FixupPrecode）。

> **MethodDesc 统一用 `MethodHandle.Value`**：`Show` 里 `mi.MethodHandle.Value` 直接返回 MethodDesc 指针，覆盖解码结果（解码拿不到 MD 时尤其有用）。这也是为什么 E9 跳板（entry 是普通跳转、不内嵌 MD）下 methodDesc 也能正常显示。

## 对应 CoreCLR 源码

| 概念 | 位置 |
|---|---|
| GetFunctionPointer 实现 | `src/coreclr/vm/runtimehandles.cpp:1306` |
| 决定返回 precode 还是真实代码 | `src/coreclr/vm/method.cpp:2230` (`TryGetMultiCallableAddrOfCode`) |
| GetNativeCode（有 precode 返回 NULL） | `src/coreclr/vm/method.cpp:1089` |
| precode 结构/数据页 | `src/coreclr/vm/precode.h`（`StubPrecodeData`/`FixupPrecodeData`） |
| precode 机器码模板 | `src/coreclr/vm/amd64/thunktemplates.asm` |
| 从入口反查 MethodDesc | `precode.h:745` (`GetPrecodeFromEntryPoint`) |

> .NET Framework 的 precode 模板与 CoreCLR 同源（同一套 thunktemplates.asm），因此解析方案与 CoreCLR 保持一致：x64 直接复用，x86 匹配 `A1 <槽> FF 25 <槽>`（StubPrecode）/ `FF 25 <槽> A1 <槽>`（FixupPrecode）。
