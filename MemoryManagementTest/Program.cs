using Unmanaged.Collections;
using Unmanaged.Extensions;

namespace Unmanaged
{
    class Program
    {
        unsafe static void Main(string[] args)
        {
            Test[] array1 = new Test[10000000];
            Object<Test>[] array2 = new Array<Object<Test>[]>(1);

            for (int index = 0; index < array1.Length; index++)
                array1[index] = new Test();

            array2.Destroy(true);

            ObjectHandler.Stop();
        }
    }
}