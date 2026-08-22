using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace readboard
{
    internal sealed class BoundedLogQueue
    {
        private readonly ConcurrentQueue<LoggingRecord> highPriority = new ConcurrentQueue<LoggingRecord>();
        private readonly ConcurrentQueue<LoggingRecord> lowPriority = new ConcurrentQueue<LoggingRecord>();
        private int count;
        private int dropCount;

        public int Count
        {
            get { return Volatile.Read(ref count); }
        }

        public int DropCount
        {
            get { return Volatile.Read(ref dropCount); }
        }

        public static bool IsHighPriority(LogLevel level)
        {
            return level >= LogLevel.Warning;
        }

        public bool TryEnqueue(LoggingRecord record)
        {
            if (record == null)
                return false;

            bool high = IsHighPriority(record.Level);
            while (true)
            {
                int current = Volatile.Read(ref count);
                if (current < LoggingLimits.QueueCapacity)
                {
                    if (Interlocked.CompareExchange(ref count, current + 1, current) != current)
                        continue;
                    if (high)
                        highPriority.Enqueue(record);
                    else
                        lowPriority.Enqueue(record);
                    return true;
                }

                if (!high)
                {
                    Interlocked.Increment(ref dropCount);
                    return false;
                }

                LoggingRecord discarded;
                if (lowPriority.TryDequeue(out discarded))
                {
                    Interlocked.Increment(ref dropCount);
                    highPriority.Enqueue(record);
                    return true;
                }

                Interlocked.Increment(ref dropCount);
                return false;
            }
        }

        public bool TryDequeue(out LoggingRecord record)
        {
            if (highPriority.TryDequeue(out record) || lowPriority.TryDequeue(out record))
            {
                Interlocked.Decrement(ref count);
                return true;
            }

            record = null;
            return false;
        }
    }
}
