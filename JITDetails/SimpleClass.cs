using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace JITDetails
{
    [StructLayout(LayoutKind.Auto)]
    class SimpleClass
    {
        private byte b1 = 1; // 1 byte 
        private byte b2 = 2; // 1 byte 
        private byte b3 = 3; // 1 byte 
        private byte b4 = 4; // 1 byte 
        private char c1 = 'A'; // 2 bytes 
        private char c2 = 'B'; // 2 bytes 
        private short s1 = 11; // 2 bytes 
        private short s2 = 12; // 2 bytes 
        private int i1 = 21; // 4 bytes 
        private long l1 = 31; // 8 bytes 
        private string str = "MyString"; // 4 bytes (only OBJECTREF) 
                                         //Total instance variable size = 28 bytes 
        static void Main1()
        {
            SimpleClass simpleObj = new SimpleClass();
            return;
        }
    }
}
