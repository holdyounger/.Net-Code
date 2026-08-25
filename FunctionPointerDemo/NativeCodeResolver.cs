using System;
using System.Reflection;

namespace FunctionPointerDemo
{
    // =====================================================================
    // NativeCodeResolver：从已 JIT 的方法解析出真实 native code（realCode）与 MethodDesc。
    // 纯解析计算逻辑，不包含任何打印/输出——与 Program.cs 的 Show 展示逻辑解耦。
    //
    // 入口：Resolve(MethodInfo, bool isFramework)
    //   ① 取 entry = mi.MethodHandle.GetFunctionPointer()
    //   ② 按运行时（.NET Framework / CoreCLR）解析 entry → 逐级跟随 precode 跳板
    //   ③ 用 mi.MethodHandle.Value（权威 MethodDesc）覆盖解析出的 MethodDesc
    //   返回 ResolveResult（IsPrecode / PrecodeType / MethodDesc / RealCode / DataPage）。
    // =====================================================================
    public static class NativeCodeResolver
    {
        public enum PrecodeKind { NotPrecode, StubPrecode, FixupPrecode, PInvokeImportPrecode, Unknown }

        public struct ResolveResult
        {
            public bool IsPrecode;
            public PrecodeKind PrecodeType;
            public IntPtr MethodDesc; // 可能为 0（非 precode 时未知）
            public IntPtr RealCode;
            public IntPtr DataPage;   // precode 数据页地址（CoreCLR）或 MethodDesc 代码区地址（.NET Framework）
        }

        // 统一入口：从已 JIT 的方法解析出 realCode / MethodDesc。
        public static ResolveResult Resolve(MethodInfo mi, bool isFramework)
        {
            IntPtr entry = mi.MethodHandle.GetFunctionPointer();
            // RuntimeMethodHandle.Value 直接就是 MethodDesc 指针（net48 / net9.0 语义一致）。
            // 相比从 stub 数据页解 MD，它不依赖 precode 模板，E9 跳板也能拿到。
            IntPtr mdHandle = mi.MethodHandle.Value;

            var result = isFramework
                ? ResolveFrameworkNativeCode(entry)
                : ResolveCoreCLRNativeCode(entry);

            // MethodHandle.Value 是权威 MethodDesc，覆盖解码结果（解码拿不到 MD 时尤其有用）
            if (mdHandle != IntPtr.Zero)
                result.MethodDesc = mdHandle;
            return result;
        }

        // =====================================================================
        // 分支 1：CoreCLR (.NET 5+ / net9.0)，默认 x64
        // =====================================================================

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
        static ResolveResult ResolveCoreCLRNativeCode(IntPtr entry)
        {
            var r = new ResolveResult { RealCode = entry };
            IntPtr cur = entry;
            int hops = 0;

            while (cur.ToInt64() != 0 && hops < 8)
            {
                long p = cur.ToInt64();
                var step = ResolveOneStepCoreCLR(p);
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
        static ResolveResult ResolveOneStepCoreCLR(long p)
        {
            var r = new ResolveResult { RealCode = (IntPtr)p };

            try
            {
                byte b0 = MemoryReader.ReadByte(p + 0);
                byte b1 = MemoryReader.ReadByte(p + 1);
                byte b2 = MemoryReader.ReadByte(p + 2);

                // 1) StubPrecode（含 PInvokeImportPrecode / ThisPtrRetBufPrecode）:
                //    4C 8B 15 <disp32>  FF 25 <disp32>
                //    数据页 StubPrecodeData { SecretParam(+0); Target(+8); Type(+16) }
                if (b0 == 0x4C && b1 == 0x8B && b2 == 0x15
                    && MemoryReader.ReadByte(p + 7) == 0xFF && MemoryReader.ReadByte(p + 8) == 0x25)
                {
                    // mov r10,[rip+disp]: data = p + 7 + disp32
                    long data = p + 7 + MemoryReader.ReadDisp32(p + 3);
                    IntPtr secret = MemoryReader.ReadPtr(data + 0); // SecretParam = MethodDesc (Stub 类型)
                    IntPtr target = MemoryReader.ReadPtr(data + 8); // Target = 真实代码 或 prestub
                    long type    = MemoryReader.ReadPtr(data + 16).ToInt64(); // Type

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
                    && MemoryReader.ReadByte(p + 6) == 0x4C && MemoryReader.ReadByte(p + 7) == 0x8B && MemoryReader.ReadByte(p + 8) == 0x15)
                {
                    // jmp [rip+disp]: data = p + 6 + disp32
                    long data = p + 6 + MemoryReader.ReadDisp32(p + 2);
                    IntPtr target   = MemoryReader.ReadPtr(data + 0);  // Target = 真实代码
                    IntPtr md       = MemoryReader.ReadPtr(data + 8);  // MethodDesc

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

        // =====================================================================
        // 分支 2：.NET Framework 4.x（net48），支持 x86 / x64
        // 与分支 1 同构：特征码匹配 + 偏移解码，不依赖 MethodDesc 内部布局。
        // =====================================================================
        //
        // .NET Framework 的 precode 模板（src/vm/i386/ / amd64/ thunktemplates.asm）：
        //   x64 与 CoreCLR 完全一致 → 直接复用 ResolveOneStepCoreCLR。
        //
        //   x86 StubPrecode:
        //     B8 <MethodDesc(4)> E9 <rel32(4)>        ; mov eax, MD ; jmp rel32
        //     → MethodDesc 内嵌在 entry+1，真实代码 = entry+10 + rel32（E9 相对跳转）
        //
        //   x86 FixupPrecode:
        //     FF 25 <target(4)> B8 <MethodDesc(4)> E9 <rel32>
        //     → target 是绝对地址（FF 25 是 jmp [addr]）
        //
        static ResolveResult ResolveFrameworkNativeCode(IntPtr entry)
        {
            var r = new ResolveResult { RealCode = entry };
            IntPtr cur = entry;
            int hops = 0;

            while (cur.ToInt64() != 0 && hops < 8)
            {
                long p = cur.ToInt64();
                var step = ResolveOneStepFramework(p);
                if (!step.IsPrecode)
                {
                    r.RealCode = cur;
                    break;
                }
                r.IsPrecode = true;
                r.PrecodeType = step.PrecodeType;
                r.MethodDesc = step.MethodDesc;
                r.DataPage = step.DataPage;
                if (step.RealCode == cur) break; // 无进展，防止死循环
                cur = step.RealCode;
                r.RealCode = cur;
                hops++;
            }
            return r;
        }

        // 单步：按当前位数选择 precode 模板做特征码匹配。
        // 模板依据 dotnet/runtime 源码（vm/i386 + vm/amd64 thunktemplates.asm）：
        //   StubPrecode:  mov r10/eax, [SecretParam槽]; jmp [Target槽]
        //                 x64 = 4C 8B 15 <disp32> FF 25 <disp32>
        //                 x86 = A1 <abs32> FF 25 <abs32>
        //   FixupPrecode: jmp [Target槽]; mov r10/eax,[MethodDesc槽]; jmp [Thunk槽]
        //                 x64 = FF 25 <disp> 4C 8B 15 <disp> FF 25 <disp>
        //                 x86 = FF 25 <abs> A1 <abs> FF 25 <abs>
        //   MethodDesc 存在数据页（entry + STUB_PAGE_SIZE + 槽偏移），不内嵌在指令里。
        //   E9/E8 相对跳转是普通跳板(trampoline)，不是 precode，MD 不内嵌。
        static ResolveResult ResolveOneStepFramework(long p)
        {
            var r = new ResolveResult { RealCode = (IntPtr)p };

            if (Environment.Is64BitProcess)
            {
                // x64：与 CoreCLR 同源，直接复用（已含 StubPrecode / FixupPrecode）。
                var core = ResolveOneStepCoreCLR(p);
                if (core.IsPrecode)
                    return core;

                // 纯跳板（jmp/call rel32）：跟随，但 MD 不内嵌。
                byte b0 = MemoryReader.ReadByte(p + 0);
                if (b0 == 0xE9 || b0 == 0xE8)
                {
                    long rel = MemoryReader.ReadDisp32(p + 1);
                    r.IsPrecode = true;
                    r.PrecodeType = PrecodeKind.Unknown; // trampoline，非标准 precode
                    r.RealCode = (IntPtr)(p + 5 + rel);
                    r.DataPage = (IntPtr)p;
                    return r;
                }

                return r;
            }

            // ---- x86 ----
            try
            {
                byte b0 = MemoryReader.ReadByte(p + 0);
                byte b1 = MemoryReader.ReadByte(p + 1);
                byte b2 = MemoryReader.ReadByte(p + 2);

                // x86 StubPrecode: A1 <SecretParam槽> FF 25 <Target槽>
                //   注意：指令里的 disp 是“槽的绝对地址”，MD/Target 要再读一次指针。
                //   布局: p+0=A1, p+1..4=SecretParam槽地址, p+5..6=FF 25, p+7..10=Target槽地址
                if (b0 == 0xA1 && MemoryReader.ReadByte(p + 5) == 0xFF && MemoryReader.ReadByte(p + 6) == 0x25)
                {
                    long mdSlotAddr = MemoryReader.ReadInt32(p + 1); // SecretParam 槽的绝对地址
                    long tgSlotAddr = MemoryReader.ReadInt32(p + 7); // Target 槽的绝对地址
                    IntPtr md     = MemoryReader.ReadPtr(mdSlotAddr);
                    IntPtr target = MemoryReader.ReadPtr(tgSlotAddr);

                    r.IsPrecode = true;
                    r.PrecodeType = PrecodeKind.StubPrecode;
                    r.MethodDesc = md;
                    r.RealCode = target;
                    r.DataPage = (IntPtr)mdSlotAddr;
                    return r;
                }

                // x86 FixupPrecode: FF 25 <Target槽> A1 <MethodDesc槽> FF 25 <Thunk槽>
                if (b0 == 0xFF && b1 == 0x25 && MemoryReader.ReadByte(p + 6) == 0xA1)
                {
                    long tgSlotAddr = MemoryReader.ReadInt32(p + 2);
                    long mdSlotAddr = MemoryReader.ReadInt32(p + 7);
                    IntPtr target = MemoryReader.ReadPtr(tgSlotAddr);
                    IntPtr md     = MemoryReader.ReadPtr(mdSlotAddr);

                    r.IsPrecode = true;
                    r.PrecodeType = PrecodeKind.FixupPrecode;
                    r.MethodDesc = md;
                    r.RealCode = target;
                    r.DataPage = (IntPtr)mdSlotAddr;
                    return r;
                }

                // x86 StubPrecode（PrecodeStub）: B8 <MethodDesc(4)> [对齐/前缀] E9 <rel32>
                //   B8 = mov eax, imm32，MethodDesc 直接内嵌在 entry+1（小端）。
                //   典型布局有多种，E9 相对跳转出现位置不一：
                //     B8 MD E9 rel                        (10B，无对齐，E9@off5)
                //     B8 MD 89 ED E9 rel                  (12B，带 mov ebp,esp，E9@off9)
                //     B8 MD 90 E8 rel32 E9 rel32          (call-through stub，E9@off11)
                //   因此在 stub 前若干字节里搜索 E9 相对跳转并跟随。
                //   注：P/Invoke 走到这里时 realCode 是 NDirect thunk（IL stub），
                //       并非 Win32 原生函数 —— 这正是本 Demo 想展示的行为。
                if (b0 == 0xB8)
                {
                    IntPtr md = (IntPtr)MemoryReader.ReadInt32(p + 1); // 内嵌 MethodDesc
                    long target = 0;
                    for (int off = 5; off <= 13; off++)
                    {
                        if (MemoryReader.ReadByte(p + off) == 0xE9)
                        {
                            long rel = MemoryReader.ReadDisp32(p + off + 1);
                            target = p + off + 5 + rel;
                            break;
                        }
                    }
                    r.IsPrecode = true;
                    r.PrecodeType = PrecodeKind.StubPrecode;
                    r.MethodDesc = md;
                    r.DataPage = (IntPtr)(p + 1); // MethodDesc 槽
                    r.RealCode = target != 0 ? (IntPtr)target : (IntPtr)p;
                    return r;
                }

                // x86 纯跳板（jmp rel32）：跟随。
                if (b0 == 0xE9)
                {
                    long rel = MemoryReader.ReadDisp32(p + 1);
                    r.IsPrecode = true;
                    r.PrecodeType = PrecodeKind.Unknown; // trampoline，非标准 precode
                    r.RealCode = (IntPtr)(p + 5 + rel);
                    r.DataPage = (IntPtr)p;
                    return r;
                }
            }
            catch (Exception) { /* 不是可读的 precode，视为直接 native code */ }

            return r;
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
    }
}
