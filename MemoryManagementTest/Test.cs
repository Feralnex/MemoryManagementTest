using System;

namespace Unmanaged
{
    public class Test
    {
        public decimal Value1 { get; set; }
        public decimal Value2 { get; set; }
        public decimal Value3 { get; set; }
        public decimal Value4 { get; set; }
        public decimal Value5 { get; set; }

        public Test()
        {
            Value1 = 1;
            Value2 = 2;
            Value3 = 3;
            Value4 = 4;
            Value5 = 5;
        }
        
        public Test(int a)
        {
            Value1 = a;
            Value2 = 2;
            Value3 = 3;
            Value4 = 4;
            Value5 = 5;
        }

        public Test(Test a)
        {
            Value1 = a.Value1;
            Value2 = 2;
            Value3 = 3;
            Value4 = 4;
            Value5 = 5;
        }

        public Test(int a, int b)
        {
            Value1 = a;
            Value2 = b;
            Value3 = 3;
            Value4 = 4;
            Value5 = 5;
        }

        public Test(Span<int> parameters)
        {
            Value1 = parameters[0];
            Value2 = parameters[1];
            Value3 = parameters[2];
            Value4 = parameters[3];
            Value5 = parameters[4];
        }
    }

    public class Test2
    {
        public decimal Value1 { get; set; }

        public Test2()
        {
            Value1 = 1;
        }

        public Test2(int a)
        {
            Value1 = a;
        }

        public Test2(Test a)
        {
            Value1 = a.Value1;
        }

        public Test2(Span<int> parameters)
        {
            Value1 = parameters[0];
        }
    }
}
