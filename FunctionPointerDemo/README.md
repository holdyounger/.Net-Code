# FunctionPointerDemo

验证 `RuntimeMethodHandle.GetFunctionPointer()` 的返回语义，以及**如何从 precode(stub) 解析出真实 native code**。

目标框架：**.NET 9 (net9.0) / x64**（CoreCLR 布局）。

## 背景

`GetFunctionPointer()` 内部走 `MethodDesc::GetMultiCallableAddrOfCode`（`src/coreclr/vm/runtimehandles.cpp`）。它**不保证**返回真实 JIT 代码，在很多情况下返回的是 **precode（stub）**：

- **FixupPrecode**（NGEN/R2R 常见）：`jmp [data.Target]` + `mov r10,[data.MethodDesc]` + `jmp [data.PrecodeFixupThunk]`
- **StubPrecode**（普通 JIT）：`mov r10,[data.SecretParam]` + `jmp [data.Target]`

要拿到**真实 native code**，需要**跟随 precode 的 Target 槽位**（即"手动取偏移"），而不是直接拿 entry。

## 运行

```bash
dotnet run -c Debug
```

（或直接在 VS 里 F5 运行 `FunctionPointerDemo`）

## 输出解读

每类方法会打印：

- `entry` —— `GetFunctionPointer()` 的原始返回值
- `isPrecode / precodeType` —— 是否为 precode 及其类型
- `methodDesc` —— 反解出的 MethodDesc 地址
- `realCode` —— 跟随 precode 后得到的**真实 native 代码**
- `entry==realCode` —— 若为 True，说明 entry 就是真实代码（无 precode）
- 各类字节 dump

实测（.NET 9.0.17 x64）中，**普通/静态/虚方法均返回 FixupPrecode**，`realCode` 是真正的 JIT 机器码（以 `55 57 48 83 EC 28` = `push rbp; push rdi; sub rsp,28h` 开头）；P/Invoke 返回 StubPrecode，`realCode` 是 NDirect IL stub（不是 Win32 目标函数）。

## 解析原理

`ResolveRealNativeCode`：

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

## 对应 CoreCLR 源码

| 概念 | 位置 |
|---|---|
| GetFunctionPointer 实现 | `src/coreclr/vm/runtimehandles.cpp:1306` |
| 决定返回 precode 还是真实代码 | `src/coreclr/vm/method.cpp:2230` (`TryGetMultiCallableAddrOfCode`) |
| GetNativeCode（有 precode 返回 NULL） | `src/coreclr/vm/method.cpp:1089` |
| precode 结构/数据页 | `src/coreclr/vm/precode.h`（`StubPrecodeData`/`FixupPrecodeData`） |
| precode 机器码模板 | `src/coreclr/vm/amd64/thunktemplates.asm` |
| 从入口反查 MethodDesc | `precode.h:745` (`GetPrecodeFromEntryPoint`) |

> 注意：本项目是 **.NET Framework 4.8 时代**的旧 CLR 布局在 `JITDetails` 项目里；本 demo 验证的是**现代 CoreCLR**（.NET 9）的布局，两者 precode 结构不同。
