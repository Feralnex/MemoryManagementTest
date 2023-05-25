using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Unmanaged.Threading;

namespace Unmanaged
{
    public class ObjectHandler
    {
        private delegate IntPtr RegisterFrozenSegmentHandler(IntPtr buffer, nint size);
        private delegate void UnregisterFrozenSegmentHandler(IntPtr segment);

        private static RegisterFrozenSegmentHandler _RegisterFrozenSegment;
        private static UnregisterFrozenSegmentHandler _UnregisterFrozenSegment;
        private static Dictionary<IntPtr, IntPtr> _objects;
        private static Queue<Action> _queue;
        private static SemaphoreSlim _semaphore;
        private static Padlock _padlock;
        private static Thread _thread;
        private static bool _active;

        public static Dictionary<IntPtr, IntPtr> Objects => _objects;

        static ObjectHandler()
        {
            MethodInfo methodInfo = typeof(GC).GetMethod(nameof(_RegisterFrozenSegment),
                BindingFlags.NonPublic | BindingFlags.Static,
                new Type[] { typeof(IntPtr), typeof(nint) })!;

            _RegisterFrozenSegment = (RegisterFrozenSegmentHandler)Delegate.CreateDelegate(typeof(RegisterFrozenSegmentHandler), methodInfo);

            methodInfo = typeof(GC).GetMethod(nameof(_UnregisterFrozenSegment),
                BindingFlags.NonPublic | BindingFlags.Static,
                new Type[] { typeof(IntPtr) })!;

            _UnregisterFrozenSegment = (UnregisterFrozenSegmentHandler)Delegate.CreateDelegate(typeof(UnregisterFrozenSegmentHandler), methodInfo);

            _objects = new Dictionary<IntPtr, IntPtr>();
            _queue = new Queue<Action>();
            _semaphore = new SemaphoreSlim(0);
            _padlock = new Padlock();
            _thread = new Thread(Destroy);
            _active = true;

            _thread.Start();
        }

        private static void Destroy()
        {
            while (_active)
            {
                _semaphore.Wait();
                _padlock.Wait();
                if (_queue.TryDequeue(out Action action))
                    action?.Invoke();
                _padlock.Release();
            }
        }

        public static void Destroy(Action destroyCallback)
        {
            _padlock.Wait();
            _queue.Enqueue(destroyCallback);
            _padlock.Release();
            _semaphore.Release();
        }

        public static void Stop()
        {
            _active = false;

            _semaphore.Release();
        }

        public static void UnregisterFrozenSegment(IntPtr segment)
           => _UnregisterFrozenSegment(segment);

        public static IntPtr RegisterFrozenSegment(IntPtr buffer, nint size)
            => _RegisterFrozenSegment(buffer, size);
    }
}