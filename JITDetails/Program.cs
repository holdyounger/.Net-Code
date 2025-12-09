using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

// All flags in the MethodDesc now reside in a single 16-bit field.

enum MethodDescClassification
{
    // Method is IL, FCall etc., see MethodClassification above.
    mdcClassification = 0x0007,
    mdcClassificationCount = mdcClassification + 1,

    // Note that layout of code:MethodDesc::s_ClassificationSizeTable depends on the exact values 
    // of mdcHasNonVtableSlot and mdcMethodImpl

    // Has local slot (vs. has real slot in MethodTable)
    mdcHasNonVtableSlot = 0x0008,

    // Method is a body for a method impl (MI_MethodDesc, MI_NDirectMethodDesc, etc)
    // where the function explicitly implements IInterface.foo() instead of foo().
    mdcMethodImpl = 0x0010,

    // Method is static
    mdcStatic = 0x0020,

    // Temporary Security Interception.
    // Methods can now be intercepted by security. An intercepted method behaves
    // like it was an interpreted method. The Prestub at the top of the method desc
    // is replaced by an interception stub. Therefore, no back patching will occur.
    // We picked this approach to minimize the number variations given IL and native
    // code with edit and continue. E&C will need to find the real intercepted method
    // and if it is intercepted change the real stub. If E&C is enabled then there
    // is no back patching and needs to fix the pre-stub.
    mdcIntercepted = 0x0040,

    // Method requires linktime security checks.
    mdcRequiresLinktimeCheck = 0x0080,

    // Method requires inheritance security checks.
    // If this bit is set, then this method demands inheritance permissions
    // or a method that this method overrides demands inheritance permissions
    // or both.
    mdcRequiresInheritanceCheck = 0x0100,

    // The method that this method overrides requires an inheritance security check.
    // This bit is used as an optimization to avoid looking up overridden methods
    // during the inheritance check.
    mdcParentRequiresInheritanceCheck = 0x0200,

    // Duplicate method. When a method needs to be placed in multiple slots in the
    // method table, because it could not be packed into one slot. For eg, a method
    // providing implementation for two interfaces, MethodImpl, etc
    mdcDuplicate = 0x0400,

    // Has this method been verified?
    mdcVerifiedState = 0x0800,

    // Is the method verifiable? It needs to be verified first to determine this
    mdcVerifiable = 0x1000,

    // Is this method ineligible for inlining?
    mdcNotInline = 0x2000,

    // Is the method synchronized
    mdcSynchronized = 0x4000,

    // Does the method's slot number require all 16 bits
    mdcRequiresFullSlotNumber = 0x8000
};
public interface MyInterface1
{
    void Method1();
    void Method2();


}
public interface MyInterface2
{
    void Method2();
    void Method3();
}
class MyClass : MyInterface1, MyInterface2
{
    public static string str = "MyString";
    public static uint ui = 0xAAAAAAAA;
    public void Method1()
    {
        Console.WriteLine("Method1");
    }

    private bool Method4()
    {
        return true;
    }

    public void Method2()
    {
        Console.WriteLine("Method2");
    }
    public virtual void Method3()
    {
        Console.WriteLine("Method3");
    }
}

class ReflectionTest
{
    private bool Method_Private()
    {
        return true;
    }
}

class Program1
{
    static void Main()
    {
        MyClass mc = new MyClass();
        MyInterface1 mi1 = mc;
        MyInterface2 mi2 = mc;
        int i = MyClass.str.Length;
        uint j = MyClass.ui;
        mc.Method1();

        Type type = typeof(ReflectionTest);
        MethodInfo OldMethod = type.GetMethod("Method_Private");
        IntPtr OldFunPtr = OldMethod.MethodHandle.GetFunctionPointer();
        Console.WriteLine($"Method1's MethodHandle:0x{OldFunPtr.ToString("x")}");
        Console.WriteLine($"Environment.Is64BitProcess's value:{Environment.Is64BitProcess}");
        Console.WriteLine($"OldMethod.IsStatic's value:{OldMethod.IsStatic}");
        Console.WriteLine($"Environment.Version.Major's value:{Environment.Version.Major}");

        if (!OldMethod.IsStatic && Environment.Version.Major == 4)
        {
            if (!Environment.Is64BitProcess)
            {
                IntPtr md = OldMethod.MethodHandle.Value;
                IntPtr mt = OldMethod.DeclaringType.TypeHandle.Value;
                IntPtr Value = (IntPtr)Marshal.ReadInt32(OldFunPtr + 1);
                if (Value == md)
                {
                    Value = (IntPtr)Marshal.ReadInt32(md + 8);
                    if (Value != null)
                    {
                        Int32 CodeOffset = (Int32)Value + 8;
                        Value = md + CodeOffset;
                        Byte ByteValue1 = Marshal.ReadByte((IntPtr)Value);
                        Byte ByteValue2 = Marshal.ReadByte((IntPtr)Value + 1);
                        if (ByteValue1 == 0x55 && ByteValue2 == 0x8B)
                        {
                            OldFunPtr = Value;
                        }
                    }
                }
            }
            else
            {
                byte ByteValue = (byte)Marshal.ReadByte(OldFunPtr);
                Console.WriteLine($"ByteValue's value:0x{ByteValue.ToString("x")}");

                if (ByteValue == 0xe9 || ByteValue == 0xe8)
                {
                    var assembly = Assembly.GetExecutingAssembly();
                    IntPtr md = Type.GetType("MyClass").GetMethod("Method1").MethodHandle.Value;
                    Console.WriteLine($"md's value:0x{md.ToString("x")}");

                    IntPtr Value = (IntPtr)Marshal.ReadInt32(md + 8);
                    Int32 CodeOffset = (Int32)Value + 8;
                    Console.WriteLine($"CodeOffset's value:0x{CodeOffset.ToString("x")}");

                    Value = md + CodeOffset;
                    Console.WriteLine($"Value's value:0x{Value.ToString("x")}");

                    Byte ByteValue1 = Marshal.ReadByte((IntPtr)Value);
                    Byte ByteValue2 = Marshal.ReadByte((IntPtr)Value + 1);
                    if ((ByteValue1 == 0x57 && ByteValue2 == 0x56) || (ByteValue1 == 0x55 && ByteValue2 == 0x57))
                    {
                        OldFunPtr = Value;
                        Console.WriteLine($"OldFunPtr's Address:0x{OldFunPtr.ToString("x")}");
                    }
                }
            }
        }

        Console.WriteLine($"OldFunPtr's Address:0x{OldFunPtr.ToString("x")}");

        mi1.Method1();
        mi1.Method2();
        mi2.Method2();
        mi2.Method3();
        mc.Method3();

        Console.ReadKey();
    }
}