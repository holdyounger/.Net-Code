using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

//
// Demo: RuntimeMethodHandle.GetFunctionPointer() 返回什么 + 如何从 stub 解析出 real native code
//
// 基于 CoreCLR 源码（dotnet/runtime, main 分支）验证：
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

        static int Main()
        {
            Console.WriteLine($"进程位数(Is64BitProcess): {Environment.Is64BitProcess}");
            Console.WriteLine($"Runtime 版本: {Environment.Version}");
            Console.WriteLine();

            var t = new Target();

            Show("普通实例方法 Add (JIT后)", t.GetType().GetMethod(nameof(Target.Add)), () => t.Add(1, 2));
            Show("静态方法 StaticAdd (JIT后)", t.GetType().GetMethod(nameof(Target.StaticAdd)), () => Target.StaticAdd(1, 2));
            Show("虚方法 VirtualAdd", t.GetType().GetMethod(nameof(Target.VirtualAdd)), () => t.VirtualAdd(1, 2));
            Show("内联小方法 Tiny", typeof(Program).GetMethod(nameof(Tiny), BindingFlags.NonPublic | BindingFlags.Static), () => Tiny(1));
            Show("P/Invoke GetTickCount", typeof(Program).GetMethod(nameof(GetTickCount), BindingFlags.NonPublic | BindingFlags.Static), () => GetTickCount());

            Console.WriteLine();
            Console.WriteLine("说明:");
            Console.WriteLine("  - entry 与 realCode 相等 → GetFunctionPointer 直接返回真实 JIT 代码（无 precode）");
            Console.WriteLine("  - entry 是 stub，realCode 由 precode 数据页 Target 解析 → 需要手动跟随偏移");
            Console.WriteLine("  - P/Invoke: realCode 是 IL stub(NDirectPrecode)，不是真正的 Win32 函数");

            return 0;
        }

        static void Show(string name, MethodInfo mi, Func<object> invoke)
        {
            // 先触发 JIT，确保方法已有 native code 可解析
            invoke();

            IntPtr entry = mi.MethodHandle.GetFunctionPointer();
            Console.WriteLine($"========== {name} ==========");
            Console.WriteLine($"  entry         = 0x{entry.ToInt64():X}");

            // 解析
            var result = ResolveRealNativeCode(entry);

            Console.WriteLine($"  isPrecode     = {result.IsPrecode}");
            if (result.IsPrecode)
                Console.WriteLine($"  precodeType   = {result.PrecodeType}");
            Console.WriteLine($"  methodDesc    = 0x{result.MethodDesc.ToInt64():X}");
            Console.WriteLine($"  realCode      = 0x{result.RealCode.ToInt64():X}");
            Console.WriteLine($"  entry==realCode= {entry == result.RealCode}");
            SafeDumpBytes(entry, "  entry bytes: ");
            if (result.IsPrecode && result.DataPage.ToInt64() != 0)
                SafeDumpBytes(result.DataPage, "  data page:   ");
            if (entry != result.RealCode)
                SafeDumpBytes(result.RealCode, "  real bytes:  ");
            Console.WriteLine();
        }

        static void SafeDumpBytes(IntPtr addr, string prefix)
        {
            try { DumpBytes(addr, prefix); }
            catch (Exception) { Console.WriteLine(prefix + "<不可读>"); }
        }

        // ---------- 解析器 ----------

        public enum PrecodeKind { NotPrecode, StubPrecode, FixupPrecode, PInvokeImportPrecode, Unknown }

        public struct ResolveResult
        {
            public bool IsPrecode;
            public PrecodeKind PrecodeType;
            public IntPtr MethodDesc; // 可能为 0（非 precode 时未知）
            public IntPtr RealCode;
            public IntPtr DataPage;   // precode 数据页地址
        }

        // x64: StubPrecodeCode (thunktemplates.asm:14)
        //   mov r10, qword ptr [rip+disp32]   ; 4C 8B 15 xx xx xx xx  → data.SecretParam
        //   jmp qword ptr [rip+disp32]        ; FF 25 xx xx xx xx     → data.Target
        //   (x64 下 mov r10 是 4C 8B 15，不是 48 8B 15)
        //
        // x64: FixupPrecodeCode (thunktemplates.asm:19)
        //   jmp qword ptr [rip+disp32]        ; FF 25 xx xx xx xx     → data.Target
        //   mov r10, qword ptr [rip+disp32]   ; 4C 8B 15 xx xx xx xx  → data.MethodDesc
        //   jmp qword ptr [rip+disp32]        ; FF 25 xx xx xx xx     → data.PrecodeFixupThunk
        //
        // 数据地址直接从指令里编码的 rip 相对偏移解码，不依赖硬编码页偏移。
        static ResolveResult ResolveRealNativeCode(IntPtr entry)
        {
            var r = new ResolveResult { RealCode = entry };
            IntPtr cur = entry;
            int hops = 0;

            while (cur.ToInt64() != 0 && hops < 8)
            {
                long p = cur.ToInt64();
                var step = ResolveOneStep(p);
                if (!step.IsPrecode)
                {
                    r.RealCode = cur;
                    break;
                }
                r.IsPrecode = true;
                r.PrecodeType = step.PrecodeType;
                r.MethodDesc = step.MethodDesc;
                r.DataPage = step.DataPage;
                // 若 Target 指向的是另一个 precode，继续跟；否则到真实代码
                if (step.RealCode == cur) break; // 无进展，防止死循环
                cur = step.RealCode;
                r.RealCode = cur;
                hops++;
            }
            return r;
        }

        // 单步：判断 entry 是否 precode，是则返回其 MethodDesc + Target
        // 数据地址直接从指令里编码的 rip 相对偏移解码（FF 25 <disp32> = jmp [rip+disp]），
        // 这样不依赖硬编码页偏移，任何页大小都正确。
        static ResolveResult ResolveOneStep(long p)
        {
            var r = new ResolveResult { RealCode = (IntPtr)p };

            try
            {
                byte b0 = ReadByte(p + 0);
                byte b1 = ReadByte(p + 1);
                byte b2 = ReadByte(p + 2);

                // 1) StubPrecode（含 PInvokeImportPrecode / ThisPtrRetBufPrecode）:
                //    4C 8B 15 <disp32>  FF 25 <disp32>
                //    数据页 StubPrecodeData { SecretParam(+0); Target(+8); Type(+16) }
                if (b0 == 0x4C && b1 == 0x8B && b2 == 0x15
                    && ReadByte(p + 7) == 0xFF && ReadByte(p + 8) == 0x25)
                {
                    // mov r10,[rip+disp]: data = p + 7 + disp32
                    long data = p + 7 + ReadDisp32(p + 3);
                    IntPtr secret = ReadPtr(data + 0); // SecretParam = MethodDesc (Stub 类型)
                    IntPtr target = ReadPtr(data + 8); // Target = 真实代码 或 prestub
                    long type    = ReadPtr(data + 16).ToInt64(); // Type

                    r.IsPrecode = true;
                    r.MethodDesc = secret;
                    r.RealCode = target;
                    r.DataPage = (IntPtr)data;
                    r.PrecodeType = ClassifyType(type);
                    return r;
                }

                // 2) FixupPrecode（NGEN/R2R，现代 .NET x64 上 GetFunctionPointer 常见返回）:
                //    FF 25 <disp32>  4C 8B 15 <disp32>  FF 25 <disp32>
                //    数据页 FixupPrecodeData { Target(+0); MethodDesc(+8); PrecodeFixupThunk(+16) }
                if (b0 == 0xFF && b1 == 0x25
                    && ReadByte(p + 6) == 0x4C && ReadByte(p + 7) == 0x8B && ReadByte(p + 8) == 0x15)
                {
                    // jmp [rip+disp]: data = p + 6 + disp32
                    long data = p + 6 + ReadDisp32(p + 2);
                    IntPtr target   = ReadPtr(data + 0);  // Target = 真实代码
                    IntPtr md       = ReadPtr(data + 8);  // MethodDesc
                    // PrecodeFixupThunk = data + 16

                    r.IsPrecode = true;
                    r.MethodDesc = md;
                    r.RealCode = target;
                    r.DataPage = (IntPtr)data;
                    r.PrecodeType = PrecodeKind.FixupPrecode;
                    return r;
                }
            }
            catch (Exception) { /* 不是可读的 precode，视为直接 native code */ }

            // 3) 其它：直接指向 native code
            return r;
        }

        static long ReadDisp32(long addr)
        {
            uint v = (uint)(ReadByte(addr) | (ReadByte(addr + 1) << 8) | (ReadByte(addr + 2) << 16) | (ReadByte(addr + 3) << 24));
            return (int)v;
        }

        static PrecodeKind ClassifyType(long type)
        {
            // precode.h: StubPrecode::Type=0x3, FixupPrecode::Type=0x2,
            //            PInvokeImportPrecode::Type=0x7, ThisPtrRetBufPrecode::Type=0x4
            switch (type & 0xFF)
            {
                case 0x03: return PrecodeKind.StubPrecode;
                case 0x02: return PrecodeKind.FixupPrecode;
                case 0x07: return PrecodeKind.PInvokeImportPrecode;
                case 0x04: return PrecodeKind.Unknown; // ThisPtrRetBufPrecode
                default:   return PrecodeKind.Unknown;
            }
        }

        // x64 + 4KB 页时 precode 数据页在代码页后一个整页（GetStubCodePageSize）
        // （保留说明：旧方案用硬编码 0x1000，现改为直接解码 rip 相对偏移，通用性更强）

        static byte ReadByte(long addr) => IsReadable(addr) ? Marshal.ReadByte((IntPtr)addr) : (byte)0;
        static IntPtr ReadPtr(long addr) => IsReadable(addr) ? Marshal.ReadIntPtr((IntPtr)addr) : IntPtr.Zero;

        // 用 VirtualQuery 检查目标页是否可读，避免 AccessViolation 直接崩进程
        [DllImport("kernel32.dll")]
        static extern int VirtualQuery(IntPtr lpAddress, out MEMORY_BASIC_INFORMATION lpBuffer, int dwLength);

        [StructLayout(LayoutKind.Sequential)]
        struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public IntPtr AllocationProtect;
            public IntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        const uint MEM_COMMIT = 0x1000;
        const uint PAGE_NOACCESS = 0x01;
        const uint PAGE_GUARD = 0x100;

        static bool IsReadable(long addr)
        {
            try
            {
                if (VirtualQuery((IntPtr)addr, out var mbi, Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
                    return false;
                if ((mbi.State & MEM_COMMIT) == 0) return false;
                uint p = mbi.Protect & 0xFF;
                if ((p & PAGE_NOACCESS) != 0) return false;
                if ((mbi.Protect & PAGE_GUARD) != 0) return false;
                return true;
            }
            catch { return false; }
        }

        static void DumpBytes(IntPtr addr, string prefix)
        {
            if (!IsReadable(addr.ToInt64()) || !IsReadable(addr.ToInt64() + 15))
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
