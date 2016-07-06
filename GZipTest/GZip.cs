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
 */
namespace GZipTest
{
    struct CompressParameters
    {
        public FileInfo originalFile;
        public string archivePath;
        public long startReadOffset, startWriteOffset;
        public int consoleCursorTop;
        public bool showMessagesInConsole;

        public CompressParameters(FileInfo originalFile, string archivePath, long startReadOffset,
            long startWriteOffset, int consoleCursorTop, bool showMessagesInConsole)
        {
            this.originalFile = originalFile;
            this.archivePath = archivePath;
            this.startReadOffset = startReadOffset;
            this.startWriteOffset = startWriteOffset;
            this.consoleCursorTop = consoleCursorTop;
            this.showMessagesInConsole = showMessagesInConsole;
        }
    }

    class GZip
    {
        /// <summary>
        /// Variable locking the console for the message output
        /// </summary>
        private static object threadConsoleLock = new object();
        /// <summary>
        /// Variable locking the file for writing
        /// </summary>
        private static object threadFileLock = new object();

        /// <summary>
        /// Buffer for GZip: 64 KB
        /// </summary>
        const int bufferSize = 4 * 1024;
        /// <summary>
        /// Using for calculating start offset for threads after first
        /// </summary>
        private static long compressedBufferSize = 0;

        // For multi-threading: optimal count of thread. 
        // User can edit this value at system variables
        static readonly int optimalThreadCount = System.Environment.ProcessorCount;
        static int threadCount = 1;

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
                    long threadCompressedSize = 0;
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
                        threadCompressedSize += readBytes;                                  // Calculate result compressed size by thread
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
                                    (threadCompressedSize * 100) / (inFile.Length/threadCount), 
                                    // / optimalThreadCount),  // % of compressed by thread devided to size that thread must compress
                                    threadCompressedSize.ToString(),
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

        public static void Decompress(FileInfo archive, string file)
        {
            try
            {
                // Get the stream of the source file.
                using (FileStream inFile = archive.OpenRead())
                {
                    // Get original file extension, for example
                    // "doc" from report.doc.gz.
                    //string curFile = archive.FullName;
                    //string origName = curFile.Remove(curFile.Length - archive.Extension.Length);

                    //Create the decompressed file.
                    using (FileStream outFile = File.Create(file))
                    {
                        using (GZipStream Decompress = new GZipStream(inFile,
                                CompressionMode.Decompress))
                        {
                            System.Console.WriteLine("Started decompressing file \"{0}\"...", archive.Name);
                            // Copy the decompression stream into the output file.
                            Decompress.CopyTo(outFile);

                            System.Console.WriteLine("Decompressed {0} to {1}", archive.Name, file);
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
}
