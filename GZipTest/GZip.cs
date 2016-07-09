using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;                        // File, FileInfo, FileStream
using System.IO.Compression;            // GZipStream
using System.Threading;                 // Thread

// Threading: start, sleep, abort, join: https://msdn.microsoft.com/en-us/library/aa645740(v=vs.71).aspx
// GZip: https://msdn.microsoft.com/ru-ru/library/system.io.compression.gzipstream(v=vs.100).aspx
// Multithreading gzip: http://peterkellner.net/2010/12/04/simple-multithreading-pattern-for-c-sharp-with-no-statics-and-gzip-compression/
// Threading: http://rsdn.ru/article/dotnet/CSThreading1.xml
// Thread: https://habrahabr.ru/post/126495/
// Ctrl+C: 
//      https://www.google.ru/webhp?sourceid=chrome-instant&ion=1&espv=2&ie=UTF-8#q=%D0%BF%D1%80%D0%B5%D1%80%D1%8B%D0%B2%D0%B0%D0%BD%D0%B8%D0%B5%20ctrl%2Bc%20%D0%B2%20%D0%BA%D0%BE%D0%BD%D1%81%D0%BE%D0%BB%D0%B8%20c%23
//      https://msdn.microsoft.com/ru-ru/library/system.console.cancelkeypress(v=vs.110).aspx

// Compressing a Very Large File: https://www.informit.com/guides/content.aspx?g=dotnet&seqNum=730

// При нажатии Ctrl+C должны останавливаться потоки (раз-)архивации.
// Console Cursor: http://codehelper.ru/questions/377/new/%D0%BA%D0%B0%D0%BA-%D0%B2-%D0%BA%D0%BE%D0%BD%D1%81%D0%BE%D0%BB%D0%B8-c-%D0%B2%D1%8B%D0%B2%D0%BE%D0%B4%D0%B8%D1%82%D1%8C-%D1%80%D0%B0%D0%B7%D0%BD%D1%8B%D0%B9-%D1%82%D0%B5%D0%BA%D1%81%D1%82-%D0%B2-%D0%BE%D0%B4%D0%BD%D0%BE%D0%B9-%D0%B8-%D1%82%D0%BE%D0%B9-%D0%B6%D0%B5-%D0%BE%D0%B1%D0%BB%D0%B0%D1%81%D1%82%D0%B8
// SeekOrigin (stream): https://msdn.microsoft.com/ru-ru/library/883dhyx0(v=vs.110).aspx
// Filestream: http://metanit.com/sharp/tutorial/5.4.php

// GZip example: https://github.com/icsharpcode/SharpZipLib/tree/master/ICSharpCode.SharpZipLib/GZip
// A Simple MultiThreading Pattern For C# With No Statics. Shows Compressing A File With GZip 
//      http://peterkellner.net/2010/12/04/simple-multithreading-pattern-for-c-sharp-with-no-statics-and-gzip-compression/


// Большой файл нужно разбивать на мелкие фрагменты
// Число потоков = числу логических процессоров
// Каждый поток должен обрабатывать некоторый buffer и записывать в файл
// Неплохо бы хранить контрольную сумму исходного файла


/*
 * Считываем общее количество байт файла
 * Делим на количество потоков - получаем рамки для каждого потока
 * С помощью offset при создании потока задаём смещение чтения
 * 
 * Остаётся вопрос записи: тут нет смещения (или есть?)
 * Можно задавать фиксированное смещение: первый поток 0-4096, второй: 4097 - 8192 и т.д.
 * Узнав количество сжатых байт - такое же смещение для потока записи
 * 
 * TODO:
 * UPD: Предполагаемый алгоритм чтения-записи в CompressThreadFunction
 * Разобраться с:
 *      чтением-записью (смещения, запись)
 *      созданием потоков
 *      синхронизацией потоков
 *      
 * ПЕРЕДЕЛАТЬ АРХИТЕКТУРУ МНОГОПОТОЧНОСТИ НА ЧИТАЮЩИЕ (n-1) И ЗАПИСЫВАЮЩИЙ (1)
 * Распределить чтение и придумать запись
 * FIFO (queue): https://msdn.microsoft.com/ru-ru/library/system.collections.queue(v=vs.110).aspx
 * 
 * 
 * Очередь Поставщик/Потребитель (Producer/Consumer): 
 *    https://rsdn.ru/article/dotnet/CSThreading1.xml
 * Практическое руководство. Синхронизация потока-производителя и потока-потребителя (Руководство по программированию на C#)
 *    https://msdn.microsoft.com/ru-ru/library/yy12yx1f(v=vs.90).aspx
 */
namespace GZipTest
{
    struct ReaderParameters
    {
        public int tid;
        public FileInfo originalFile;
        public long startReadOffset;
        public int consoleCursorTop;
        public bool showMessagesInConsole;

        public ReaderParameters(int tid, FileInfo originalFile, long startReadOffset,
            int consoleCursorTop, bool showMessagesInConsole)
        {
            this.tid = tid;
            this.originalFile = originalFile;
            this.startReadOffset = startReadOffset;
            this.consoleCursorTop = consoleCursorTop;
            this.showMessagesInConsole = showMessagesInConsole;
        }
    }

    struct WriteParameters
    {
        public string resultFilePath;
        public int consoleCursorTop;
        public bool showMessagesInConsole;

        public WriteParameters(string resultFilePath, int consoleCursorTop, bool showMessagesInConsole)
        {
            this.resultFilePath = resultFilePath;
            this.consoleCursorTop = consoleCursorTop;
            this.showMessagesInConsole = showMessagesInConsole;
        }
    }

    struct CompressParameters
    {
        public FileInfo originalFile;
        public long startReadOffset;
        public int consoleCursorTop;
        public bool showMessagesInConsole;

        public CompressParameters(FileInfo originalFile, long startReadOffset,
            int consoleCursorTop, bool showMessagesInConsole)
        {
            this.originalFile = originalFile;
            this.startReadOffset = startReadOffset;
            this.consoleCursorTop = consoleCursorTop;
            this.showMessagesInConsole = showMessagesInConsole;
        }
    }

    class WriterQueue : IDisposable
    {
        /// <summary>
        /// Signal for queue
        /// </summary>
        EventWaitHandle wh = new AutoResetEvent(false);
        Thread worker;
        public Thread WorkerThread { get { return worker; } }
        /// <summary>
        /// Locker for queue
        /// </summary>
        object locker = new object();
        /// <summary>
        /// Locker for queue
        /// </summary>
        object consoleLocker;
        Queue<Byte[]> tasks = new Queue<byte[]>();

        int readThreadCount = 0;
        int numberOfWaitingThread = 0;
        public int NumberOfWaitingThread { get { return numberOfWaitingThread; } }

        /// <summary>
        /// How many bytes already writed to file
        /// </summary>
        private long bytesWrited = 0;
        /// <summary>
        /// Property. Contains how many bytes already writed to file
        /// </summary>
        public long BytesWrited { get { return bytesWrited; } }

        public WriterQueue(string resultFilePath, int readThreadCount, int consoleCursorTop, bool showMessagesInConsole, object consoleLock)
        {
            this.readThreadCount = readThreadCount;
            this.consoleLocker = consoleLock;
            WriteParameters wPar = new WriteParameters(resultFilePath, consoleCursorTop, showMessagesInConsole);
            worker = new Thread(Work);
            worker.Name = "Write Thread";
            worker.IsBackground = true;
            worker.Start(wPar);
        }

        public bool EnqueueTask(byte[] task, int threadNumber)
        {
            if (threadNumber == numberOfWaitingThread)
            {
                lock (locker)
                {
                    tasks.Enqueue(task);
                }
                wh.Set();           // Signal what queue get new task
                numberOfWaitingThread++;
                if (numberOfWaitingThread == readThreadCount)
                {
                    numberOfWaitingThread = 0;
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        private void EnqueueTask(byte[] task)
        {
            lock (locker)
            {
                tasks.Enqueue(task);
            }
            wh.Set();               // Signal what queue get new task
        }

        public void Dispose()
        {
            EnqueueTask(null);      // Signal for Writer to finishing
            worker.Join();          // Wait for ending Writer
            wh.Close();             // Free resources
        }

        void Work(object objParameters)
        {
            WriteParameters parameters;
            if (objParameters != null)
            {
                parameters = (WriteParameters)objParameters;
            }
            else
            {
                throw new Exception("Can't start compress writer-thread without parameters");
            }
            try
            {
                using (FileStream outFile = File.Create(parameters.resultFilePath))
                {
                    while (true)
                    {
                        byte[] task = null;

                        lock (locker)
                        {
                            if (tasks.Count > 0)
                            {
                                task = tasks.Dequeue();
                                if (task == null)
                                {
                                    return;
                                }
                            }
                        }

                        if (task != null)
                        {
                            outFile.Write(task, 0, task.Length);                        // Write compressed bytes to file
                            bytesWrited = outFile.Length;                               // Save the value of current archive length

                            if (parameters.showMessagesInConsole)
                            {
                                lock (consoleLocker)
                                {
                                    System.Console.CursorTop = parameters.consoleCursorTop;
                                    System.Console.CursorLeft = 0;
                                    System.Console.Write(" - {0}: Writed {1} bytes",   // - Write Thread: Writed 1000 bytes
                                        Thread.CurrentThread.Name,
                                        outFile.Length.ToString());
                                }
                            }
                        }
                        else                // No tasks
                        {
                            wh.WaitOne();   // Wait for a signal
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
    }

    class GZip
    {
        /// <summary>
        /// Variable locking the console for the message output
        /// </summary>
        private static object threadConsoleLock = new object();

        /// <summary>
        /// Buffer for GZip: 64 KB
        /// </summary>
        const int bufferSize = 64 * 1024;

        // For multi-threading: optimal count of thread. 
        // User can edit this value at system variables
        static readonly int optimalThreadCount = System.Environment.ProcessorCount;
        static int threadCount = 0;
        // ------------------------------------------------------------

        static WriterQueue writer;
        // PRODUCER-CONSUMER
        /// <summary>
        /// compressStream file to archive using optimal number of threads
        /// </summary>
        /// <param name="fi">FileInfo of original file</param>
        /// <param name="archivePath">Path to resulting archive</param>
        /// <param name="showMessagesInConsole">Show compress info messages at console?</param>
        /// <returns>Size of result archive in bytes</returns>
        public static long Compress(FileInfo fi, string archivePath, bool showMessagesInConsole = true)
        {
            try
            {
                // Get the stream of the source file.
                using (FileStream inFile = fi.OpenRead())
                {
                    // Prevent compressing hidden and already compressed files.
                    if ((File.GetAttributes(fi.FullName) & FileAttributes.Hidden)
                        != FileAttributes.Hidden & fi.Extension != ".gz")
                    {
                        if (showMessagesInConsole)
                        {
                            System.Console.WriteLine("Started compressing file \"{0}\"...", fi.Name);
                        }
                        int currentCursorTop = Console.CursorTop;                                              // Save console cursor row position
                        // Create new WriterQueue and start his thread
                        writer = new WriterQueue(archivePath, optimalThreadCount - 1,
                            currentCursorTop + optimalThreadCount - 1, showMessagesInConsole, threadConsoleLock);

                        Thread[] threadPool = new Thread[optimalThreadCount - 1];                              // Create thread array
                        ReaderParameters readerParameters;
                        for (int i = 0; i < optimalThreadCount - 1; i++)                                       // Creating read threads
                        {                                                                                      // (optimalThreadCount - 1) because 1 thread for writing
                            // Parameters for thread
                            readerParameters = new ReaderParameters(i, fi, i * bufferSize,
                                currentCursorTop + i, showMessagesInConsole);

                            threadPool[i] = new Thread(CompressThreadReader);
                            threadPool[i].Name = "Read Thread #" + (i + 1);
                            threadPool[i].IsBackground = true;
                            threadPool[i].Start(readerParameters);
                            //threadCount++;
                        }
                        // Waiting for all threads
                        for (int i = 0; i < optimalThreadCount - 1; i++)
                        {
                            threadPool[i].Join();
                        }
                        // Say writer to finish work. 
                        writer.Dispose();
                        //writer.WorkerThread.Join(1000);
                        //writer.EnqueueTask(null, writer.NumberOfWaitingThread);
                        // Result
                        if (showMessagesInConsole)
                        {
                            lock (threadConsoleLock)
                            {
                                System.Console.WriteLine("\n\nCompressed {0} from {1} to {2} bytes.",
                                    fi.Name,
                                    fi.Length.ToString(),
                                    writer.BytesWrited);
                            }
                        }
                        return writer.BytesWrited;
                    }
                    else
                    {
                        throw new Exception("File is hidden or already compressed");
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                // Closing and memory free area
                //writer.Dispose();
                GC.Collect();
            }
        }

        public static long Decompress(FileInfo archive, string filePath, bool showMessagesInConsole = true)
        {
            try
            {
                // Get the stream of the source file.
                using (FileStream inFile = archive.OpenRead())
                {
                    if (showMessagesInConsole)
                    {
                        System.Console.WriteLine("Started decompressing file \"{0}\"...", archive.Name);
                    }
                    int currentCursorTop = Console.CursorTop;                                              // Save console cursor row position
                    // Create new WriterQueue and start his thread
                    writer = new WriterQueue(filePath, optimalThreadCount - 1,
                        currentCursorTop + optimalThreadCount - 1, showMessagesInConsole, threadConsoleLock);

                    Thread[] threadPool = new Thread[optimalThreadCount - 1];                              // Create thread array
                    ReaderParameters readerParameters;
                    for (int i = 0; i < optimalThreadCount - 1; i++)                                       // Creating read threads
                    {                                                                                      // (optimalThreadCount - 1) because 1 thread for writing
                        // Parameters for thread
                        readerParameters = new ReaderParameters(i, archive, i * bufferSize,
                            currentCursorTop + i, showMessagesInConsole);

                        threadPool[i] = new Thread(DecompressThreadReader);
                        threadPool[i].Name = "Read Thread #" + (i + 1);
                        threadPool[i].IsBackground = true;
                        threadPool[i].Start(readerParameters);
                        //threadCount++;
                    }
                    // Waiting for all threads
                    for (int i = 0; i < optimalThreadCount - 1; i++)
                    {
                        threadPool[i].Join();
                    }
                    // Say writer to finish work.
                    //writer.WorkerThread.Join(1000); 
                    //writer.EnqueueTask(null, writer.NumberOfWaitingThread);
                    writer.Dispose();
                    // Result
                    if (showMessagesInConsole)
                    {
                        lock (threadConsoleLock)
                        {
                            System.Console.WriteLine("\n\nDecompressed {0} from {1} to {2} bytes.",
                                archive.Name,
                                archive.Length.ToString(),
                                writer.BytesWrited);
                        }
                    }
                    return writer.BytesWrited;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                // Closing and memory free area
                //writer.Dispose();
                GC.Collect();
            }
        }

        private static void CompressThreadReader(object objParameters)
        {
            try
            {
                threadCount++;
                ReaderParameters parameters;
                if (objParameters != null)
                {
                    parameters = (ReaderParameters)objParameters;
                }
                else
                {
                    throw new Exception("Can't start compress-thread without parameters");
                }
                // While not end of file
                using (FileStream inFile = parameters.originalFile.OpenRead())
                {
                    // Memory stream as buffer
                    using (MemoryStream memory = new MemoryStream())
                    {
                        using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, true))
                        {
                            int readBytes = 0;
                            long threadCompressedSize = 0;
                            do // while (inFile.Position < inFile.Length)
                            {
                                byte[] buffer = new byte[bufferSize];
                                readBytes = inFile.Read(buffer, 0, buffer.Length);
                                if (readBytes == 0)
                                { 
                                    break;
                                }
                                gzip.Write(buffer, 0, readBytes);

                                // Create task: enqueue compressed bytes using memory.ToArray()
                                // Try until task be enqueued
                                bool isSuccess = false;
                                do
                                {
                                    isSuccess = writer.EnqueueTask(memory.ToArray(), parameters.tid);
                                } while (!isSuccess);

                                // Reset memory stream
                                byte[] memoryBuffer = memory.GetBuffer();
                                Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                memory.Position = 0;
                                memory.SetLength(0);

                                threadCompressedSize += readBytes;                                  // Calculate result compressed size by thread
                                // Compressed => change offset to next
                                // Offset for buffer = 4096: 
                                // b(4096)*2 = 8192      b*3 = 12288      b*4 = 16384  
                                //    3 threads  00000	04095	t1 | 2 threads  00000	04095	t1
                                //               04096	08191	t2 |            04096	08191	t2
                                //               08192	12287	t3 |            08192	12287	t1
                                //               12288	16383	t1 |            12288	16383	t2
                                //               16384	20479	t2 |            16384	20479	t1
                                //    t1: 4095 -> 12288 = +8193    | t1: 4095 -> 8192 = +4097
                                // Offset: [(threadCount - 1) * bufSize(4096)] + 1
                                //   offset: (3-1)*4096 +1 = 8193 | offset: (2-1)*4096 +1 = 4097

                                // Seek for new position
                                //if (parameters.compressionMode == CompressionMode.Compress)
                                //{
                                //    inFile.Seek((optimalThreadCount - 2) * bufferSize + 1,              // Offset for thread of read file, 1 thread for write -> (tCount - 1)
                                //        SeekOrigin.Current);
                                //}
                                if (parameters.showMessagesInConsole)
                                {
                                    // Console info:
                                    lock (threadConsoleLock)
                                    {
                                        System.Console.CursorTop = parameters.consoleCursorTop;
                                        System.Console.CursorLeft = 0;
                                        System.Console.Write(" - {0}: Completed {1}%, {2}/{3} bytes",   // Thread #1: Completed 0%, 0/10000 bytes
                                            Thread.CurrentThread.Name,
                                            (threadCompressedSize * 100) / (inFile.Length / threadCount),
                                            // / optimalThreadCount),  // % of compressed by thread devided to size that thread must compress
                                            threadCompressedSize.ToString(),
                                            inFile.Length / threadCount);
                                    }
                                }
                            }
                            while (readBytes > 0);                            
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nError: {0}", ex.Message);
            }
            finally
            {
                threadCount--;
            }
        }

        private static void DecompressThreadReader(object objParameters)
        {
            try
            {
                threadCount++;
                ReaderParameters parameters;
                if (objParameters != null)
                {
                    parameters = (ReaderParameters)objParameters;
                }
                else
                {
                    throw new Exception("Can't start decompress-thread without parameters");
                }
                // While not end of file
                using (FileStream inFile = parameters.originalFile.OpenRead())
                {
                    // Memory stream as buffer
                    using (MemoryStream memory = new MemoryStream())
                    {
                        using (GZipStream gzip = new GZipStream(inFile, CompressionMode.Decompress, true))
                        {
                            int readBytes = 0;
                            long threadProcessedSize = 0;
                            do //while (inFile.Position < inFile.Length)
                            {
                                byte[] buffer = new byte[bufferSize];
                                //gzip.CopyTo(memory, bufferSize);
                                //gzip.Read(buffer, 0, buffer.Length);
                                readBytes = gzip.Read(buffer, 0, buffer.Length);
                                if (readBytes == 0)
                                {
                                    break;
                                }

                                //if (readBytes > 0)
                                //{
                                memory.Write(buffer, 0, readBytes);
                                //}

                                // Create task: enqueue compressed bytes using memory.ToArray()
                                // Try until task be enqueued
                                bool isSuccess = false;
                                do
                                {
                                    isSuccess = writer.EnqueueTask(memory.ToArray(), parameters.tid);
                                } while (!isSuccess);

                                // Reset memory stream
                                byte[] memoryBuffer = memory.GetBuffer();
                                Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                memory.Position = 0;
                                memory.SetLength(0);

                                threadProcessedSize += readBytes;                                       // Calculate result compressed size by thread
                                // Compressed => change offset to next
                                // Offset for buffer = 4096: 
                                // b(4096)*2 = 8192      b*3 = 12288      b*4 = 16384  
                                //    3 threads  00000	04095	t1 | 2 threads  00000	04095	t1
                                //               04096	08191	t2 |            04096	08191	t2
                                //               08192	12287	t3 |            08192	12287	t1
                                //               12288	16383	t1 |            12288	16383	t2
                                //               16384	20479	t2 |            16384	20479	t1
                                //    t1: 4095 -> 12288 = +8193    | t1: 4095 -> 8192 = +4097
                                // Offset: [(threadCount - 1) * bufSize(4096)] + 1
                                //   offset: (3-1)*4096 +1 = 8193 | offset: (2-1)*4096 +1 = 4097

                                // Seek for new position
                                //if (parameters.compressionMode == CompressionMode.Compress)
                                //{
                                //    inFile.Seek((optimalThreadCount - 2) * bufferSize + 1,            // Offset for thread of read file, 1 thread for write -> (tCount - 2)
                                //        SeekOrigin.Current);
                                //}
                                if (parameters.showMessagesInConsole)
                                {
                                    // Console info:
                                    lock (threadConsoleLock)
                                    {
                                        System.Console.CursorTop = parameters.consoleCursorTop;
                                        System.Console.CursorLeft = 0;
                                        System.Console.Write(" - {0}: Completed {1}%, {2}/{3} bytes",   // Thread #1: Completed 0%, 0/10000 bytes
                                            Thread.CurrentThread.Name,
                                            (threadProcessedSize * 100) / (inFile.Length / threadCount),
                                            // / optimalThreadCount),  // % of compressed by thread devided to size that thread must compress
                                            threadProcessedSize.ToString(),
                                            inFile.Length / threadCount);
                                    }
                                }
                            }
                            while (readBytes > 0);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nError: {0}", ex.Message);
            }
            finally
            {
                threadCount--;
            }
        }

        //---------------------------------------------------------------------------------------------------------------
        // OLD CODE
        /*
        /// <summary>
        /// Variable locking the file for writing
        /// </summary>
        private static object threadFileLock = new object();

        /// <summary>
        /// Using for calculating start offset for threads after first
        /// </summary>
        private static long compressedBufferSize = 0;

        // File streams
        private static FileStream outFile; // = File.OpenWrite(parameters.archivePath);            // Open created in main function archive file for write
        private static GZipStream compressStream;

        /// <summary>
        /// compressStream file to archive using optimal number of threads
        /// </summary>
        /// <param name="fi">FileInfo of original file</param>
        /// <param name="archivePath">Path to resulting archive</param>
        /// <param name="showMessagesInConsole">Show compress info messages at console?</param>
        /// <returns>Size of result archive in bytes</returns>
        public static long Compress(FileInfo fi, string archivePath, bool showMessagesInConsole = true)
        {
            try
            {
                // Get the stream of the source file.
                using (FileStream inFile = fi.OpenRead())
                {
                    // Prevent compressing hidden and already compressed files.
                    if ((File.GetAttributes(fi.FullName) & FileAttributes.Hidden)
                        != FileAttributes.Hidden & fi.Extension != ".gz")
                    {
                        if (showMessagesInConsole)
                        {
                            System.Console.WriteLine("Started compressing file \"{0}\"...", fi.Name);
                        }
                        // Create the compressed file.
                        //outFile = File.OpenWrite(parameters.archivePath);            
                        outFile = File.Create(archivePath);                                         // Created archive file for write
                        compressStream = new GZipStream(outFile, CompressionMode.Compress);

                        int currentCursorTop = Console.CursorTop;
                        // Start first thread
                        CompressParameters compressParameters =
                            new CompressParameters(fi, archivePath,
                                0, 0,
                                currentCursorTop, showMessagesInConsole);
                        Thread[] threadPool = new Thread[optimalThreadCount];
                        threadPool[0] = new Thread(CompressThreadFunction);
                        threadPool[0].Name = "Thread #1";
                        threadPool[0].IsBackground = true;
                        threadPool[0].Start(compressParameters);
                        // Wait for compressed size from first thread for offset
                        while (compressedBufferSize == 0)
                        {
                            Thread.Sleep(100);
                        }
                        // Next threads
                        for (int i = 1; i < optimalThreadCount; i++)
                        {
                            // Parameters for threads
                            compressParameters = new CompressParameters(fi, archivePath,
                                i * bufferSize, i * compressedBufferSize,                           // Надо ли смещать на i*buff +1 ?
                                currentCursorTop + i, showMessagesInConsole);

                            threadPool[i] = new Thread(CompressThreadFunction);
                            threadPool[i].Name = "Thread #" + (i + 1);
                            threadPool[i].IsBackground = true;
                            threadPool[i].Start(compressParameters);
                            threadCount++;
                        }
                        // Waiting for all threads
                        for (int i = 0; i < optimalThreadCount; i++)
                        {
                            threadPool[i].Join();
                        }
                        // Result
                        if (showMessagesInConsole)
                        {
                            System.Console.WriteLine("\nCompressed {0} from {1} to {2} bytes.",
                                fi.Name,
                                fi.Length.ToString(),
                                outFile.Length.ToString());
                        }
                        return outFile.Length;
                    }
                    else
                    {
                        throw new Exception("File is hidden or already compressed");
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
            finally
            {
                // Closing and memory free area
                if (compressStream != null)
                {
                    compressStream.Close();
                    compressStream.Dispose();
                }
                if (outFile != null)
                {
                    outFile.Close();
                    outFile.Dispose();
                }
                GC.Collect();
            }
        }
        */
        /*
        // Algorithm:
        // - Open read and write files in streams
        // - Read to buffer
        // - compressStream
        // - Write to result file
        // - Shift the position of the read and write streams
        // - Retry while not end of file
        private static void CompressThreadFunction(object objParameters)
        {
            try
            {
                CompressParameters parameters;
                if (objParameters != null)
                {
                    parameters = (CompressParameters)objParameters;
                }
                else
                {
                    throw new Exception("Can't start compress thread without parameters");
                }
                using (FileStream inFile = parameters.originalFile.OpenRead())
                {
                    long threadWriteOffset = parameters.startWriteOffset;

                    inFile.Seek(parameters.startReadOffset, SeekOrigin.Begin);              // Start offset for thread from begin of read file
                    //FileStream outFile = File.OpenWrite(parameters.archivePath);          // Open created in main function archive file for write
                    //outFile.Seek(parameters.startWriteOffset, SeekOrigin.Begin);            // Start offset for thread from begin of write file
                    //GZipStream Compress = new GZipStream(outFile, CompressionMode.Compress);

                    //long startPosition = parameters.startWriteOffset;
                    long lastPosition = parameters.startWriteOffset;
                    long newPosition = 0;
                    long threadProcessedSize = 0;
                    long writeOffset = 0;
                    int readBytes = 0;
                    // While not end of file
                    while (inFile.Position < inFile.Length)
                    {
                        byte[] buffer = new byte[bufferSize];
                        readBytes = inFile.Read(buffer, 0, buffer.Length);

                        // Lock for writing file
                        lock (threadFileLock)
                        {
                            outFile.Position = lastPosition;                                // Load value of stream position for this thread
                            outFile.Seek(writeOffset, SeekOrigin.Current);                  // Offset for thread from last position of write file
                            compressStream.Write(buffer, 0, buffer.Length);                 // Compress buffer and save to result file
                            newPosition = outFile.Position;                                 // Save value of stream position for this thread
                        }
                        compressedBufferSize = newPosition - lastPosition;                  // Calculate offset to global variable
                        lastPosition = newPosition;                                         // Save value of stream position for this thread
                        // Compressed => change offset to next
                        // Offset for buffer = 4096: 
                        // b(4096)*2 = 8192      b*3 = 12288      b*4 = 16384  
                        //    4 threads  00000	04095	t1 | 2 threads  00000	04095	t1
                        //               04096	08191	t2 |            04096	08191	t2
                        //               08192	12287	t3 |            08192	12287	t1
                        //               12288	16383	t4 |            12288	16383	t2
                        //               16384	20479	t1 |            16384	20479	t1
                        //    t1: 4095 -> 16384 = +12289   | t1: 4095 -> 8192 = +4097
                        // Offset: [(threadCount - 1) * bufSize(4096)] + 1
                        //   offset: (4-1)*4096 +1 = 12289 | offset: (2-1)*4096 +1 = 4097
                        writeOffset = (optimalThreadCount - 1) * compressedBufferSize + 1;  // Offset for thread of write file
                        threadProcessedSize += readBytes;                                  // Calculate result compressed size by thread
                        inFile.Seek((optimalThreadCount - 1) * bufferSize + 1,              // Offset for thread of read file
                            SeekOrigin.Current);
                        if (parameters.showMessagesInConsole)
                        {
                            // Console info:
                            lock (threadConsoleLock)
                            {
                                System.Console.CursorTop = parameters.consoleCursorTop;
                                System.Console.CursorLeft = 0;
                                System.Console.Write(" - {0}: Completed {1}%, {2}/{3} bytes",   // Thread #1: Completed 0%, 0/10000 bytes
                                    Thread.CurrentThread.Name,
                                    (threadProcessedSize * 100) / (inFile.Length / threadCount),
                                    // / optimalThreadCount),  // % of compressed by thread devided to size that thread must compress
                                    threadProcessedSize.ToString(),
                                    inFile.Length.ToString());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\nError: {0}", ex.Message);
            }
            finally
            {
                threadCount--;
                //
            }
        }
        */
    }
}
