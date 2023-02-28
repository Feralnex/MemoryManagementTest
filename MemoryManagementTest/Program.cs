namespace MemoryManagementTest
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Test[] firstArray = new Test[10000000];
            Object<Test>[] secondArray = new Object<Object<Test>[]>(1);

            for (int index = 0; index < firstArray.Length; index++)
                firstArray[index] = new Test();

            for (int index = 0; index < secondArray.Length; index++)
                secondArray[index] = new Object<Test>();

            for (int index = 0; index < secondArray.Length; index++)
                secondArray[index].Destroy();

            secondArray.Destroy();
        }
    }
}