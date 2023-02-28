namespace MemoryManagementTest
{
    public static class UnmanagedExtensions
    {
        public static void Destroy<TValue>(this TValue value)
        {
            Object<TValue> unmanagedValue = (Object<TValue>)value;

            unmanagedValue.Destroy();
        }

        public static void CopyTo<TValue>(this IEnumerable<TValue> source, Object<TValue>[] destination, int index)
        {
            foreach (TValue item in source)
            {
                destination[index++] = item;
            }
        }
    }
}
