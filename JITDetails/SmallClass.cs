using System;
class SmallClass
{
    private byte[] _largeObj;
    public SmallClass(int size)
    {
        _largeObj = new byte[size];
        _largeObj[0] = 0xAA;
        _largeObj[1] = 0xBB;
        _largeObj[2] = 0xCC;
    }
    public byte[] LargeObj
    {
        get
        {
            return this._largeObj;
        }
    }
}
class Program
{
    public static void Main1(string[] args)
    {
        SmallClass smallObj = Program.Create(84930, 10, 15, 20, 25);
        return;
    }
    static SmallClass Create(int size1, int size2, int size3, int size4, int size5)
    {
        int objSize = size1 + size2 + size3 + size4 + size5;
        SmallClass smallObj = new SmallClass(objSize);
        return smallObj;
    }
}