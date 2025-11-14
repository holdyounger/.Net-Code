using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
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
    public void Method2()
    {
        Console.WriteLine("Method2");
    }
    public virtual void Method3()
    {
        Console.WriteLine("Method3");
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

        MethodInfo OldMethod = Assembly.GetExecutingAssembly().GetType("MyClass").GetMethod("Method1");
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