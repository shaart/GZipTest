using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;                        // File, FileInfo, FileStream
using System.IO.Compression;            // GZipStream
using System.Threading;                 // Thread

#region LINKS_AND_NOTES
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
#endregion

namespace GZipTest
{
    class GZip
    {
        #region GLOBAL_VARIABLES
        /// <summary>
        /// Variable locking the console for the message output
        /// </summary>
        private static object threadConsoleLock = new object();
        /// <summary>
        /// Buffer for GZip: 64 KB
        /// </summary>
        const int bufferSize = 64 * 1024;
        /// <summary>
        /// For multi-threading: optimal count of thread. 
        /// User can edit this value at system variables
        /// </summary>
        static readonly int optimalThreadCount = System.Environment.ProcessorCount;
        /// <summary>
        /// Current number of read threads
        /// </summary>
        static int threadCount = 0;
        /// <summary>
        /// Class WriterQueue - thread for writing to file
        /// </summary>
        static WriterQueue writer;
        #endregion
        // ------------------------------------------------------------
        #region STANDART_GZIP_COMPRESS_AND_DECOMPRESS_BLOCK
        private static string directoryPath = @"d:\dump";
        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileToCompress"></param>
        public static void DefaultCompress(FileInfo fileToCompress)
        {
            using (FileStream originalFileStream = fileToCompress.OpenRead())
            {
                if ((File.GetAttributes(fileToCompress.FullName) &
                   FileAttributes.Hidden) != FileAttributes.Hidden & fileToCompress.Extension != ".gz")
                {
                    using (FileStream compressedFileStream = File.Create(fileToCompress.FullName + ".gz"))
                    {
                        using (GZipStream compressionStream = new GZipStream(compressedFileStream,
                           CompressionMode.Compress))
                        {
                            originalFileStream.CopyTo(compressionStream);

                        }
                    }
                    FileInfo info = new FileInfo(directoryPath + "\\" + fileToCompress.Name + ".gz");
                    Console.WriteLine("Compressed {0} from {1:N} to {2:N} bytes.",
                    fileToCompress.Name, fileToCompress.Length.ToString(), info.Length.ToString());
                }

            }
        }

        /// <summary>
        /// Standart decompress
        /// </summary>
        /// <param name="fileToDecompress">FileInfo of decompressing file</param>
        public static void DefaultDecompress(FileInfo fileToDecompress)
        {
            using (FileStream originalFileStream = fileToDecompress.OpenRead())
            {
                string currentFileName = fileToDecompress.FullName;
                string newFileName = currentFileName.Remove(currentFileName.Length - fileToDecompress.Extension.Length);

                using (FileStream decompressedFileStream = File.Create(newFileName))
                {
                    using (GZipStream decompressionStream = new GZipStream(originalFileStream, CompressionMode.Decompress))
                    {
                        decompressionStream.CopyTo(decompressedFileStream);
                        Console.WriteLine("Decompressed: {0}", fileToDecompress.Name);
                    }
                }
            }
        }
        #endregion
        // ------------------------------------------------------------
        #region PRODUCER-CONSUMER_REGION

        #region COMPRESS_BLOCK
        /// <summary>
        /// Compresses file to archive using optimal number of threads
        /// </summary>
        /// <param name="fi">FileInfo of original file</param>
        /// <param name="archivePath">Full path to resulting archive</param>
        /// <param name="showMessagesInConsole">Should the output messages to the console</param>
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
                        int currentCursorTop = Console.CursorTop;                           // Save console cursor row position
                        // Create new WriterQueue and start his thread
                        writer = new WriterQueue(archivePath, optimalThreadCount - 1,
                            currentCursorTop + optimalThreadCount - 1, showMessagesInConsole, threadConsoleLock);

                        Thread[] threadPool = new Thread[optimalThreadCount - 1];           // Create thread array
                        ReaderParameters readerParameters;
                        for (int i = 0; i < optimalThreadCount - 1; i++)                    // Creating read threads
                        {                                                                   // (optimalThreadCount - 1) because 1 thread for writing
                            // Parameters for thread
                            readerParameters = new ReaderParameters(i, fi, i * bufferSize,
                                currentCursorTop + i, showMessagesInConsole);

                            threadPool[i] = new Thread(CompressThreadReader);
                            threadPool[i].Name = "Read Thread #" + (i + 1);
                            threadPool[i].IsBackground = true;
                            threadPool[i].Start(readerParameters);
                        }
                        // Waiting for all threads
                        for (int i = 0; i < optimalThreadCount - 1; i++)
                        {
                            threadPool[i].Join();
                        }
                        // Say writer to finish work. 
                        writer.Dispose();                                                   // Call dispose of writer thread, wait for end of his work
                        //writer.WorkerThread.Join(1000);
                        //writer.EnqueueTask(null, writer.NumberOfWaitingThread);
                        // Result
                        if (showMessagesInConsole)
                        {
                            lock (threadConsoleLock)
                            {
                                System.Console.WriteLine("\n\nCompressed {0} from {1:N} to {2:N} bytes.",
                                    fi.Name,
                                    fi.Length,
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

        /// <summary>
        /// Thread for reading, compressing data and sending it to Queue for writing to result file
        /// </summary>
        /// <param name="objParameters">Struct "ReaderParameters"</param>
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
                            long threadProcessedSize = 0;
                            long threadWritedToMemorySize = 0;                                      //debug
                            byte[] buffer = new byte[bufferSize];
                            do // while (inFile.Position < inFile.Length)
                            {
                                readBytes = inFile.Read(buffer, 0, buffer.Length);
                                if (readBytes == 0)
                                {
                                    break;
                                }
                                gzip.Write(buffer, 0, readBytes);

                                /*
                                // For debug
                                using (var outFile2 = File.OpenWrite("dump_readed.txt"))
                                {
                                    outFile2.Seek(0, SeekOrigin.End);
                                    outFile2.Write(buffer, 0, readBytes);
                                }
                                */
                                // Create task: enqueue compressed bytes using memory.ToArray()
                                // Try until task be enqueued
                                bool isSuccess = false;
                                do
                                {
                                    // Bug: queue don't process last data
                                    isSuccess = writer.EnqueueTask(memory.ToArray(), parameters.tid);
                                    //isSuccess = writer.EnqueueTask(buffer, parameters.tid);       // For debug
                                } while (!isSuccess);

                                // Reset memory stream
                                byte[] memoryBuffer = memory.GetBuffer();
                                /*
                                // For debug
                                using (var outFile3 = File.OpenWrite("dump_memory.txt"))
                                {
                                    outFile3.Seek(0, SeekOrigin.End);
                                    outFile3.Write(memoryBuffer, 0, memoryBuffer.Length);
                                }
                                */
                                threadWritedToMemorySize += memoryBuffer.Length;                    // Calculate
                                Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                memory.Position = 0;
                                memory.SetLength(0);
                                memory.Capacity = 0;
                                memory.Flush();

                                threadProcessedSize += readBytes;                                  // Calculate result compressed size by thread
                                // Compressed => change offset to next
                                // Offset for buffer = 4096: 
                                // b(4096)*2 = 8192      b*3 = 12288      b*4 = 16384  
                                //    3 threads  00000	04095	t1 | 2 threads  00000	04095	t1
                                //               04096	08191	t2 |            04096	08191	t2
                                //               08192	12287	t3 |            08192	12287	t1
                                //               12288	16383	t1 |            12288	16383	t2
                                //               16384	20479	t2 |            16384	20479	t1
                                //    t1: 4096 -> 12288 = +8192    | t1: 4096 -> 8192 = +4096
                                // Offset: [(threadCount - 1) * bufSize(4096)]
                                //   offset: (3-1)*4096 = 8192     | offset: (2-1)*4096 = 4096

                                // Seek for new position
                                inFile.Seek((optimalThreadCount - 2) * bufferSize,              // Offset for thread of read file,
                                    SeekOrigin.Current);                                        // 1 thread for write -> (tCount - 1)

                                if (parameters.showMessagesInConsole)
                                {
                                    // Console info:
                                    lock (threadConsoleLock)
                                    {
                                        System.Console.CursorTop = parameters.consoleCursorTop;
                                        System.Console.CursorLeft = 0;
                                        System.Console.Write(" - {0}: Completed {1}%, {2:N}/{3:N} bytes",   // Thread #1: Completed 0%, 0/10000 bytes
                                            Thread.CurrentThread.Name,
                                            (threadProcessedSize * 100) / (inFile.Length / threadCount),
                                            // / optimalThreadCount),  // % of compressed by thread devided to size that thread must compress
                                            threadProcessedSize,
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
        #endregion

        #region DECOMPRESS_BLOCK
        /// <summary>
        /// Decompresses archive to file using optimal number of threads
        /// </summary>
        /// <param name="archive">FileInfo of archive</param>
        /// <param name="filePath">Full path to uncompressed file</param>
        /// <param name="showMessagesInConsole">Should the output messages to the console</param>
        /// <returns>Size of result file in bytes</returns>
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
                            System.Console.WriteLine("\n\nDecompressed {0} from {1:N} to {2:N} bytes.",
                                archive.Name,
                                archive.Length,
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
        /// <summary>
        /// Thread for reading, decompressing data and sending it to Queue for writing to result file
        /// </summary>
        /// <param name="objParameters">Struct "ReaderParameters"</param>
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
                            byte[] buffer = new byte[bufferSize];
                            do //while (inFile.Position < inFile.Length)
                            {
                                //gzip.CopyTo(memory, bufferSize);
                                //gzip.Read(buffer, 0, buffer.Length);
                                readBytes = gzip.Read(buffer, 0, buffer.Length);
                                if (readBytes == 0)
                                {
                                    break;
                                }

                                memory.Write(buffer, 0, readBytes);

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
                                memory.Capacity = 0;
                                memory.Flush();

                                threadProcessedSize += readBytes;                                       // Calculate result compressed size by thread
                                // Compressed => change offset to next
                                // Offset for buffer = 4096: 
                                // b(4096)*2 = 8192      b*3 = 12288      b*4 = 16384  
                                //    3 threads  00000	04095	t1 | 2 threads  00000	04095	t1
                                //               04096	08191	t2 |            04096	08191	t2
                                //               08192	12287	t3 |            08192	12287	t1
                                //               12288	16383	t1 |            12288	16383	t2
                                //               16384	20479	t2 |            16384	20479	t1
                                //    t1: 4096 -> 12288 = +8192    | t1: 4096 -> 8192 = +4096
                                // Offset: [(threadCount - 1) * bufSize(4096)]
                                //   offset: (3-1)*4096 = 8192 | offset: (2-1)*4096 = 4096

                                // Seek for new position
                                inFile.Seek((optimalThreadCount - 2) * bufferSize,                      // Offset for thread of read file,
                                    SeekOrigin.Current);                                                // 1 thread for write -> (tCount - 2)

                                if (parameters.showMessagesInConsole)
                                {
                                    // Console info:
                                    lock (threadConsoleLock)
                                    {
                                        System.Console.CursorTop = parameters.consoleCursorTop;
                                        System.Console.CursorLeft = 0;
                                        System.Console.Write(" - {0}: Completed {1}%, {2:N}/{3:N} bytes", // Thread #1: Completed 0%, 0/10 000.00 bytes
                                            Thread.CurrentThread.Name,
                                            (threadProcessedSize * 100) / (inFile.Length / threadCount),  // % of compressed by thread devided to size
                                            threadProcessedSize,                                          // that thread must compress
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
        #endregion

        #endregion
    }
}
