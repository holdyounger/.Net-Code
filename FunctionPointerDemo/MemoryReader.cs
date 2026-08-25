using System;
using System.Runtime.InteropServices;

namespace FunctionPointerDemo
{
    // 内存读取原语：统一用 Marshal.Read* 读原生内存；IsReadable 保护避免读到不可读地址抛异常。
    // 被 NativeCodeResolver（解析计算）与 DumpMethodDesc（打印 MethodDesc raw）共用。
    internal static class MemoryReader
    {
        // ReadDisp32 语义：解码有符号 32 位 rip 相对偏移，Marshal.ReadInt32 正好返回有符号 int。
        public static long ReadDisp32(long addr) => ReadInt32(addr);

        public static int ReadInt32(long addr)
            => IsReadable(addr) ? Marshal.ReadInt32((IntPtr)addr) : 0;

        public static byte ReadByte(long addr) => IsReadable(addr) ? Marshal.ReadByte((IntPtr)addr) : (byte)0;

        public static IntPtr ReadPtr(long addr) => IsReadable(addr) ? Marshal.ReadIntPtr((IntPtr)addr) : IntPtr.Zero;

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

        public static bool IsReadable(long addr)
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
    }
}
