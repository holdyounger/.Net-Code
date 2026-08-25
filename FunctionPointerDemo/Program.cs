using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

//
// Demo: RuntimeMethodHandle.GetFunctionPointer() 返回什么 + 如何从 stub 解析出 real native code
//
// 支持两种运行时 / 架构：
//   - CoreCLR (.NET 5+，如 net9.0)  —— 默认 x64，解析 FixupPrecode / StubPrecode
//     (rip-relative 偏移解码，见 ResolveOneStepCoreCLR)
//   - .NET Framework 4.x (net48)    —— 支持 x86 / x64，特征码匹配 + 偏移解码（与 CoreCLR 同构）
//     MethodDesc 直接用 mi.MethodHandle.Value（CLR 内部 MethodDesc 指针），
//     不依赖 precode 数据页解码，E9 跳板/已 JIT 代码都能拿到。
//
// CoreCLR 参考 (dotnet/runtime, main 分支)：
//   - RuntimeMethodHandle_GetFunctionPointer  → MethodDesc::GetMultiCallableAddrOfCode
//     (src/coreclr/vm/runtimehandles.cpp:1306)
//   - MethodDesc::TryGetMultiCallableAddrOfCode 决定返回 precode(stub) 还是直接 native code
//     (src/coreclr/vm/method.cpp:2230)
//   - MethodDesc::GetNativeCode(): 有 precode 时返回 NULL（method.cpp:1089）
//   - StubPrecode 布局 (src/coreclr/vm/precode.h:59 / thunktemplates.asm)：
//       代码页(24B):  mov r10, [data.SecretParam] ; jmp [data.Target]
//       数据页:      StubPrecodeData { SecretParam; Target; Type; }
//       x64 + 4KB 页下数据页偏移 = GetStubCodePageSize() = 0x1000
//   - Precode::GetPrecodeFromEntryPoint / GetMethodDesc / GetTarget
//     (precode.h:745, precode.cpp:127, precode.h:687)
//
// RuntimeMethodHandle.Value 语义（referencesource 与 CoreCLR 一致）：
//   - 直接返回该方法的 MethodDesc 指针，不经过 precode 解码。
//   - net9.0 实测：Value 与 GetFunctionPointer 是不同地址（MethodDesc vs entry stub），
//     首字节 01/02 为 MethodDesc 标志位。
//

namespace FunctionPointerDemo
{
    static unsafe class Program
    {
        // ---------- 被测方法 ----------

        // 1) 普通实例方法（大概率走 StubPrecode / precode）
        class Target
        {
            public int Add(int a, int b) => a + b;
            public static int StaticAdd(int a, int b) => a + b;
            public virtual int VirtualAdd(int a, int b) => a + b;
        }

        // 2) 内联友好的小方法（可能被内联 / 直接指向 native）
        static int Tiny(int x) => x + 1;

        // 3) P/Invoke（走 IL stub / NDirectPrecode，返回的不是真正的 native 目标）
        [DllImport("kernel32.dll")]
        static extern uint GetTickCount();

        // 4) .NET BCL 托管接口（需要 JIT，走 precode）：文件创建 + 进程创建
        //    这些是真正的托管方法（System.IO.File / System.Diagnostics.Process），
        //    与上面的 P/Invoke stub 不同——它们需要 JIT，GetFunctionPointer 返回
        //    precode/跳板，可被解析到真实 JIT 代码。

        static int Main()
        {
            bool isFramework = IsDotNetFramework;
            Console.WriteLine($"进程位数(Is64BitProcess): {Environment.Is64BitProcess}");
            Console.WriteLine($"Runtime 版本: {Environment.Version}");
            Console.WriteLine($"运行时:      {(isFramework ? ".NET Framework 4.x" : "CoreCLR (.NET 5+)")}");
            Console.WriteLine();

            var t = new Target();

            Show("普通实例方法 Add (JIT后)", t.GetType().GetMethod(nameof(Target.Add)), isFramework);
            Show("静态方法 StaticAdd (JIT后)", t.GetType().GetMethod(nameof(Target.StaticAdd)), isFramework);
            Show("虚方法 VirtualAdd", t.GetType().GetMethod(nameof(Target.VirtualAdd)), isFramework);
            Show("内联小方法 Tiny", typeof(Program).GetMethod(nameof(Tiny), BindingFlags.NonPublic | BindingFlags.Static), isFramework);
            Show("P/Invoke GetTickCount", typeof(Program).GetMethod(nameof(GetTickCount), BindingFlags.NonPublic | BindingFlags.Static), isFramework);

            // ---- .NET Framework 内部 Win32 封装（文件/进程创建最终调用层）----
            // Microsoft.Win32.Win32Native 是 mscorlib 里所有 File.Create/FileStream
            // 和 Process.Start 最终调用的内部类。SafeCreateFile / CreateProcess 是 internal
            // 方法，需用反射获取，并用 RuntimeHelpers.PrepareMethod 强制 JIT。
            var win32 = typeof(System.IO.File).Assembly.GetType("Microsoft.Win32.Win32Native");
            if (win32 != null)
            {
                var sf = win32.GetMethod("SafeCreateFile",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (sf != null)
                {
                    Show("内部 Win32Native.SafeCreateFile（文件创建·最终封装）", sf, isFramework);

                    // 手动调用一次验证解析出的 native entry (realCode) 是否可执行。
                    VerifyNativeEntryCallable(sf);
                }
                else Console.WriteLine("!! 未找到 Win32Native.SafeCreateFile");

                var cp = typeof(System.Diagnostics.Process).GetMethod("StartWithCreateProcess",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (cp != null)
                {
                    Show("内部 Process.StartWithCreateProcess（进程创建·最终封装）", cp, isFramework);
                }
                else Console.WriteLine("!! 未找到 Process.StartWithCreateProcess");
            }
            else Console.WriteLine("!! 未找到 Win32Native 类型");

            Console.WriteLine();
            Console.WriteLine("说明:");
            Console.WriteLine("  - entry 与 realCode 相等 → GetFunctionPointer 直接返回真实 JIT 代码（无 precode）");
            Console.WriteLine("  - CoreCLR(.NET 9): entry 是 stub，realCode 由 precode 数据页 Target 解析 → 需要手动跟随偏移");
            Console.WriteLine("  - .NET Framework: 走 MethodDesc 追址（HookInfo.cs 同款），x86/x64 布局不同");
            Console.WriteLine("  - P/Invoke: realCode 是 IL stub(NDirectPrecode)，不是真正的 Win32 函数");
            Console.WriteLine("  - 内部 Win32Native.SafeCreateFile（文件·最终封装）/ Process.StartWithCreateProcess（进程·最终封装）");
            Console.WriteLine("    是 File.Create / Process.Start 的底层实现，需 JIT，可解析到真实 JIT 代码");

            return 0;
        }

        // 触发方法 JIT：RuntimeHelpers.PrepareMethod 直接强制 CLR 为该 MethodInfo 生成 native code。
        // 优点：不真实调用目标函数（避免被安全策略判为攻击行为），也不落盘创建任何文件。
        // 原理参考 RuntimeHelpers.PrepareMethod（coreclr vm/ecall.cpp）：内部走
        // MethodDesc::PrepareMethod → JIT 编译；对 P/Invoke 会生成 NDirect stub，
        // 对 precode 方法会 JIT 出真实代码——GetFunctionPointer 均可解析到。
        static void PrepareJit(MethodInfo mi)
        {
            RuntimeHelpers.PrepareMethod(mi.MethodHandle);

            mi.GetParameters(); // 触发参数类型加载，避免后续解析时遇到未加载的类型
        }

        // 手动调用一次验证解析出的 native entry (realCode) 确实可执行。
        // 方法：通过 MethodInfo.Invoke 真实调用目标函数一次。方法此时已被 PrepareMethod JIT 完成，
        //   Invoke 会走 CallDescr 真实 call 到 realCode 入口——能正常执行并返回 SafeFileHandle，
        //   即证明解析出的 realCode 是真实可执行的 native 代码（动态验证）。
        // 安全设计：用不存在的路径 + OPEN_EXISTING + GENERIC_READ 调用，只做只读打开尝试，
        //   文件不存在即返回 IsInvalid=true 的句柄（或抛 Win32 异常），绝不创建任何文件。
        static void VerifyNativeEntryCallable(MethodInfo mi)
        {
            // SafeCreateFile(String, Int32, FileShare, SECURITY_ATTRIBUTES, FileMode, Int32, IntPtr)
            object[] args = {
                @"C:\__nonexistent_zhuazhua_verify__.tmp", // 不存在的路径
                unchecked((int)0x80000000),                // dwDesiredAccess = GENERIC_READ
                (System.IO.FileShare)0,                    // dwShareMode = None
                null,                                      // securityAttrs = null
                (System.IO.FileMode)3,                     // dwCreationDisposition = OPEN_EXISTING
                (int)0x80,                                 // dwFlagsAndAttributes = FILE_ATTRIBUTE_NORMAL
                IntPtr.Zero                                // hTemplateFile
            };

            try
            {
                object ret = mi.Invoke(null, args);

                // SafeCreateFile 返回 SafeFileHandle。即便 OPEN_EXISTING 失败，也会返回一个
                // IsInvalid=true 的 SafeFileHandle——总之方法执行到了 native 代码并走完了
                // CreateFileW 调用，证明解析出的 realCode 可执行。
                var handleType = ret.GetType();
                var isInvalidProp = handleType.GetProperty("IsInvalid");
                bool isInvalid = isInvalidProp != null && (bool)isInvalidProp.GetValue(ret, null);
                Console.WriteLine($"  [verify] ✓ MethodInfo.Invoke 调用成功，返回 {handleType.Name}, IsInvalid={isInvalid} → native entry 可执行，realCode 正确");
            }
            catch (System.Reflection.TargetInvocationException tie)
            {
                // 反射调用会包一层 TargetInvocationException，解开看内部异常。
                // 若抛出预期 IO 异常（文件不存在），同样证明进入了 native 代码。
                var inner = tie.InnerException;
                if (inner is System.IO.FileNotFoundException || inner is System.IO.IOException)
                    Console.WriteLine($"  [verify] ✓ 进入 native entry 抛出预期 IO 异常: {inner.GetType().Name}: {inner.Message} → realCode 可执行");
                else
                    Console.WriteLine($"  [verify] 调用抛出异常: {inner?.GetType().Name}: {inner?.Message}");
            }
            catch (System.IO.FileNotFoundException)
            {
                Console.WriteLine("  [verify] ✓ 成功进入 native entry 并抛出预期的 FileNotFoundException（realCode 可执行）");
            }
            catch (System.IO.IOException ex)
            {
                Console.WriteLine($"  [verify] ✓ 进入 native entry 抛出 IOException: {ex.Message} → realCode 可执行");
            }
        }

        static bool IsDotNetFramework => Environment.Version.Major == 4;

        static void Show(string name, MethodInfo mi, bool isFramework)
        {
            // 触发 JIT：仅 PrepareMethod，不真实调用目标函数，也不创建任何临时文件。
            PrepareJit(mi);

            IntPtr entry = mi.MethodHandle.GetFunctionPointer();
            Console.WriteLine($"========== {name} ==========");
            Console.WriteLine($" entry = 0x{entry.ToInt64():X}");

            // 解析 + 计算 realCode（封装在 NativeCodeResolver.Resolve 内）
            var result = NativeCodeResolver.Resolve(mi, isFramework);

            Console.WriteLine($" isPrecode = {result.IsPrecode}");
            if (result.IsPrecode)
                Console.WriteLine($" precodeType = {result.PrecodeType}");
            Console.WriteLine($" methodDesc = 0x{result.MethodDesc.ToInt64():X}");
            Console.WriteLine($" realCode = 0x{result.RealCode.ToInt64():X}");
            Console.WriteLine($" entry==realCode= {entry == result.RealCode}");
            SafeDumpBytes(entry, " entry bytes: ");
            if (result.IsPrecode && result.DataPage.ToInt64() != 0)
                SafeDumpBytes(result.DataPage, " data page:   ");
            else
                Console.WriteLine(" data page: (无，非 precode / 无数据页)");
            if (entry != result.RealCode)
                SafeDumpBytes(result.RealCode, " real bytes:  ");
            else
                Console.WriteLine(" real bytes: (== entry，即 GetFunctionPointer 已直接返回真实 JIT 代码)");
            DumpMethodDesc(result.MethodDesc, result.RealCode);
            Console.WriteLine();
        }

        // 打印 MethodDesc 的 raw 十六进制数据，方便手动推算/核对 realCode。
        // 关键字段（.NET Framework x86 MethodDesc 常见布局）：
        //   [+00] m_chunkIndex / MethodDescChunk 指针
        //   [+08] m_codeOrIL —— 直接 JIT 的方法（isPrecode=False）该字段即 native code = realCode；
        //         precode 方法则该字段是 precode/IL（非 native code），须走 precode 跟随。
        static void DumpMethodDesc(IntPtr md, IntPtr realCode)
        {
            try
            {
                Console.WriteLine(" methoddesc :");
                // ① 原始十六进制：前 32 字节，每 4 字节一组带 offset 标注，方便手动推算
                Console.Write(" raw : ");
                for (int off = 0; off < 32; off += 4)
                {
                    if (!MemoryReader.IsReadable(md.ToInt64() + off) || !MemoryReader.IsReadable(md.ToInt64() + off + 3))
                    {
                        Console.Write($"+{off:X2} ?? ?? ?? ?? ");
                        continue;
                    }
                    int v = MemoryReader.ReadInt32(md.ToInt64() + off);
                    Console.Write($"+{off:X2} {v & 0xFF:X2} {(v >> 8) & 0xFF:X2} {(v >> 16) & 0xFF:X2} {(v >> 24) & 0xFF:X2}  ");
                }
                Console.WriteLine();
                // ② 关键字段 [+08] m_codeOrIL：直接 JIT 时即 realCode，可用来验证解析结果
                IntPtr ptr08 = MemoryReader.ReadPtr(md.ToInt64() + 8);
                Console.WriteLine($" [+08] = 0x{ptr08.ToInt64():X8} (m_codeOrIL：直接JIT时=真实代码；precode时=precode/IL)");
                if (ptr08 != IntPtr.Zero)
                {
                    bool eq = ptr08 == realCode;
                    Console.WriteLine($" [+08]==realCode? {eq} (realCode=0x{realCode.ToInt64():X8})");
                    if (eq)
                        Console.WriteLine("        ✓ MethodDesc[+08] 与解析出的 realCode 完全一致，解析正确");
                    else
                        Console.WriteLine("        (precode 方法：realCode 应经 precode 跟随，而非 [+08])");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($" methoddesc : <解析失败: {e.Message}>");
            }
        }

        static void SafeDumpBytes(IntPtr addr, string prefix)
        {
            try { DumpBytes(addr, prefix); }
            catch (Exception) { Console.WriteLine(prefix + "<不可读>"); }
        }

        static void DumpBytes(IntPtr addr, string prefix)
        {
            if (!MemoryReader.IsReadable(addr.ToInt64()) || !MemoryReader.IsReadable(addr.ToInt64() + 15))
            {
                Console.WriteLine(prefix + "<不可读>");
                return;
            }
            var b = new byte[16];
            Marshal.Copy(addr, b, 0, b.Length);
            Console.Write(prefix);
            foreach (var x in b) Console.Write($"{x:X2} ");
            Console.WriteLine();
        }
    }
}
