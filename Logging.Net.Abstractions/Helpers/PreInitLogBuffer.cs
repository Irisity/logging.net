using System;
using System.Collections.Generic;

namespace Logging.Net.Abstractions.Helpers
{
    /// <summary>
    /// A small bounded buffer for log calls made before a log implementation has been configured.
    /// </summary>
    /// <remarks>
    /// Static ILog instances are typically created — and sometimes written to — before the program
    /// reaches its logging initialization. Without this buffer those events, which are usually the
    /// startup failures one most wants to see, would be discarded with no trace at all.
    /// The buffer holds at most <see cref="Capacity"/> events and drops the oldest when full.
    /// Note that replayed events are handed to the implementation at replay time, so they carry the
    /// timestamp of the initialization rather than the moment they were originally logged.
    /// </remarks>
    internal static class PreInitLogBuffer
    {
        internal const int Capacity = 256;

        private static readonly Queue<Pending> pending = new Queue<Pending>();
        private static bool replaying;

        /// <summary>
        /// Buffers a log call that could not be routed because no implementation is configured yet.
        /// </summary>
        public static void Add(ILog log, int level, string message, Exception exception)
        {
            lock (pending)
            {
                if (replaying)
                {
                    // A replayed event whose implementation still cannot be resolved. Re-buffering it
                    // would loop forever, so drop it instead.
                    return;
                }
                if (pending.Count >= Capacity)
                {
                    pending.Dequeue();
                }
                pending.Enqueue(new Pending(log, level, message, exception));
            }
        }

        /// <summary>
        /// Replays and clears everything buffered so far. Safe to call re-entrantly; nested calls are ignored.
        /// </summary>
        public static void Replay()
        {
            lock (pending)
            {
                if (replaying || pending.Count == 0)
                {
                    return;
                }
                replaying = true;
            }

            try
            {
                while (true)
                {
                    Pending[] batch;
                    lock (pending)
                    {
                        if (pending.Count == 0)
                        {
                            return;
                        }
                        batch = pending.ToArray();
                        pending.Clear();
                    }

                    foreach (var entry in batch)
                    {
                        try
                        {
                            entry.Log.Log(entry.Level, entry.Message, entry.Exception);
                        }
                        catch (Exception)
                        {
                            // Replaying must never throw into whoever happened to configure the accessor.
                        }
                    }
                }
            }
            finally
            {
                lock (pending)
                {
                    replaying = false;
                }
            }
        }

        /// <summary>
        /// Discards everything buffered. Intended for tests that reset the static accessor.
        /// </summary>
        internal static void Clear()
        {
            lock (pending)
            {
                pending.Clear();
            }
        }

        private struct Pending
        {
            public readonly ILog Log;
            public readonly int Level;
            public readonly string Message;
            public readonly Exception Exception;

            public Pending(ILog log, int level, string message, Exception exception)
            {
                Log = log;
                Level = level;
                Message = message;
                Exception = exception;
            }
        }
    }
}
