using System.Collections.Generic;
using System.Threading;

namespace Unmanaged.Threading
{
    public class Padlock
    {
        private object _queuePadlock;
        private object _ticketPadlock;
        private volatile int _ticketsCount;
        private volatile int _ticketToRide;

        public int Count
        {
            get
            {
                lock (_ticketPadlock)
                {
                    if (_ticketsCount < _ticketToRide) return 0;
                    else if (_ticketsCount == _ticketToRide) return 1;
                    else return _ticketsCount - _ticketToRide + 1;
                }
            }
        }

        public Padlock()
        {
            _queuePadlock = new object();
            _ticketPadlock = new object();
            _ticketsCount = 0;
            _ticketToRide = 1;
        }

        public int Reserve()
        {
            lock (_ticketPadlock)
            {
                return Interlocked.Increment(ref _ticketsCount);
            }
        }

        public void Wait()
        {
            int ticket = Reserve();

            Wait(ticket);
        }

        public void Wait(int ticket)
        {
            Monitor.Enter(_queuePadlock);

            while (ticket != _ticketToRide) Monitor.Wait(_queuePadlock);
        }

        public void Release()
        {
            Interlocked.Increment(ref _ticketToRide);

            Monitor.PulseAll(_queuePadlock);
            Monitor.Exit(_queuePadlock);
        }
    }

    public class Padlock<TKey> : Padlock where TKey : notnull
    {
        private object _queuePadlock;
        private Dictionary<TKey, Padlock> _padlocks;

        public Padlock()
        {
            _queuePadlock = new object();
            _padlocks = new Dictionary<TKey, Padlock>();
        }

        public int Reserve(TKey key)
        {
            lock (_queuePadlock)
            {
                if (!_padlocks.TryGetValue(key, out Padlock padlock))
                {
                    padlock = new Padlock();

                    _padlocks.Add(key, padlock);
                }

                return padlock.Reserve();
            }
        }

        public void Wait(TKey key)
        {
            int ticket = Reserve(key);
            Padlock padlock = _padlocks[key];

            padlock.Wait(ticket);
        }

        public void Wait(TKey key, int ticket)
        {
            Padlock padlock = _padlocks[key];

            padlock.Wait(ticket);
        }

        public void Release(TKey key)
        {
            lock (_queuePadlock)
            {
                if (_padlocks.TryGetValue(key, out Padlock padlock))
                {
                    padlock.Release();

                    if (padlock.Count == 0) _padlocks.Remove(key);
                }
            }
        }
    }
}
