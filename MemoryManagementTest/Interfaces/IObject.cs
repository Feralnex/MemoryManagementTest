using System;

namespace Unmanaged.Interfaces
{
    public interface IObject
    {
        event Action<bool> Destroyed;

        void Destroy(bool blocking = false);
    }
}