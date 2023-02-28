namespace MemoryManagementTest
{
    public interface IObject
    {
        event Action Destroyed;

        void Destroy();
    }
}