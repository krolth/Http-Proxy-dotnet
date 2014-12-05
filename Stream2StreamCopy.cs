using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HttpProxy
{
    class Stream2StreamCopy
    {
        // Allocate 1MB in 4K Chunks for use by all of our outgoing chunks
        static int _readChunkSize = 4 * 1024;
        static int _maxChunksInTransit;
        const int MaxBuffersPerRequest = 40;

        // This  queue is shared by all the requests in our system. 
        // It will have be initialized by MaxChunksInTransit and ReadChunkSize
        private static BlockingCollection<ArraySegment<byte>> _freeReadBufferQueue;

        public static void Init(int bufferSize)
        {
            _readChunkSize = bufferSize;
            _maxChunksInTransit = (1048576 / _readChunkSize);

            _freeReadBufferQueue = new BlockingCollection<ArraySegment<byte>>();
            for (int i = 0; i < _maxChunksInTransit; i++)
            {
                _freeReadBufferQueue.Add(new ArraySegment<byte>(new byte[_readChunkSize]));
            }
        }

        public static async Task CopyStreamsAsync(Stream input, Stream output)
        {
            var dataQueue = new BlockingCollection<ArraySegment<byte>>();

            // We create a semaphore per request. This helps us ensure a large request does not dominate all the small requests
            var semaphore = new SemaphoreSlim(MaxBuffersPerRequest);
            int pendingWrites = 0;

            var allWritesHaveBeenEnqueued = new TaskCompletionSource<object>();

            // Init the write loop. It will await the dataQueue. Once there is data to write it will 
            Action writeLoop = async () =>
            {
                while (true)
                {
                    ArraySegment<byte> dataArraySegment = dataQueue.Take();

                    int dataRead = dataArraySegment.Count;
                    byte[] dataBuffer = dataArraySegment.Array;
                    if (dataRead == 0)
                    {
                        // If the dataQueue has no data it means we have reached the end of the input stream. Time to bail out of the write loop
                        _freeReadBufferQueue.Add(new ArraySegment<byte>(dataBuffer));
                        break;
                    }

                    Interlocked.Increment(ref pendingWrites);

                    // Schedule async write. Make sure you don't wait until it's done before continuing 
                    output.BeginWrite(dataBuffer, 0, dataRead, (iar) =>
                    {
                        try
                        {
                            output.EndWrite(iar);
                            Interlocked.Decrement(ref pendingWrites);
                        }
                        finally
                        {
                            // Enqueue the buffer back to the pool so that we can read more data
                            _freeReadBufferQueue.Add(new ArraySegment<byte>(dataBuffer));
                        }

                        // Notify the reader that it can schedule more reads
                        semaphore.Release();


                    }, null);
                }
                allWritesHaveBeenEnqueued.SetResult(null);
            };

            Action readLoop = async () =>
            {
                while (true)
                {
                    // Make sure you are not hogging all the buffers within this request
                    await semaphore.WaitAsync();

                    // Now wait until there is a free buffer to read into.
                    ArraySegment<byte> freeBuffer = _freeReadBufferQueue.Take();
                    byte[] readBuffer = freeBuffer.Array;
                    int readBufferSize = readBuffer.Length;

                    // Fill the read buffer before enqueueing it for writing
                    int readTotal = 0;
                    int readImmediate;
                    do
                    {
                        readImmediate = await input.ReadAsync(readBuffer, readTotal, readBufferSize - readTotal);
                        readTotal += readImmediate;
                    }
                    while (readImmediate > 0 && readTotal < readBufferSize);

                    // Enqueue it in the dataQueue for this request
                    dataQueue.Add(new ArraySegment<byte>(readBuffer, 0, readTotal));

                    if (readTotal == 0)
                    {
                        // Done reading. Bail out of the read loop
                        break;
                    }
                }
            };

            Task.Run(readLoop);
            Task.Run(writeLoop);

            await allWritesHaveBeenEnqueued.Task;
        }
    }
}