using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
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
        /// Buffer for GZip: 128 KB
        /// </summary>
        const int bufferSize = 128 * 1024;
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
                // Closing and compressedMemory free area
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
                        int readBytes = 0;
                        byte[] buffer = new byte[bufferSize];
                        long threadProcessedSize = 0;
                        long threadWritedToMemorySize = 0;                                      //debug
                        do // while (inFile.Position < inFile.Length)
                        {
                            readBytes = inFile.Read(buffer, 0, buffer.Length);
                            if (readBytes == 0)
                            {
                                break;
                            }
                            byte[] readBuffer = new byte[readBytes];
                            Array.Copy(buffer, 0, readBuffer, 0, readBytes);                // Copy 64 Kb array to array with length = read bytes

                            using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, true))
                            {
                                gzip.Write(readBuffer, 0, readBytes);
                            } // END OF GZipStream (gzip)
                            if (memory.Length == 0)
                            {
                                throw new Exception("Data did not compressed");
                            }
                            #region COMMENTED
                            /*
                                // For debug
                                using (var outFile2 = File.OpenWrite("dump_readed.txt"))
                                {
                                    outFile2.Seek(0, SeekOrigin.End);
                                    outFile2.Write(buffer, 0, readBytes);
                                }
                                */
                            // Create task: enqueue compressed bytes using compressedMemory.ToArray()
                            // Try until task be enqueued
                            #endregion
                            bool isSuccess = false;
                            do
                            {
                                // Bug: queue don't process last data
                                isSuccess = writer.EnqueueTask(memory.ToArray(), parameters.tid);
                                //isSuccess = writer.EnqueueTask(buffer, parameters.tid);       // For debug
                            } while (!isSuccess);

                            // Reset compressedMemory stream
                            byte[] memoryBuffer = memory.GetBuffer();
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
                            //inFile.Seek((optimalThreadCount - 2) * bufferSize,              // Offset for thread of read file,
                            //    SeekOrigin.Current);                                        // 1 thread for write -> (tCount - 1)
                            inFile.Position = inFile.Position + (optimalThreadCount - 2) * bufferSize;

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
                            } // END OF if
                        } // END OF WHILE
                        while (readBytes > 0);
                    } // END OF MemoryStream (memory)
                } // END OF FileStream (inFile)
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
                // Closing and compressedMemory free area
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
            // String.IndexOf search result: -1
            const int NOT_FOUND = -1;
            // String bytes of compress block start
            const string COMPRESSED_BLOCK_START = "‹\b\0\0\0\0\0\0";
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
                    int nextLeftBlockNumber = -1;   // For multithread
                    int nextRightBlockNumber = 0;   // For multithread
                    int leftBlockNumber = -1;
                    int rightBlockNumber = 0;
                    int firstIndex = NOT_FOUND;
                    int secondIndex = NOT_FOUND;
                    long threadProcessedSize = 0;
                    do // START OF MAIN WHILE: 
                    {  //   inFile.Position < inFile.Length
                        // Memory stream as buffer
                        using (MemoryStream compressedMemory = new MemoryStream())
                        {
                            firstIndex = NOT_FOUND;
                            secondIndex = NOT_FOUND;
                            int readCompressedBytes = 0;
                            nextLeftBlockNumber = nextLeftBlockNumber + (optimalThreadCount - 1);   // first loop: 0 => at multithread change to threads offset (threadNum)
                            nextRightBlockNumber = nextRightBlockNumber + (optimalThreadCount - 1); // first loop: 1 => see line above ^
                            // get compressed block for decompress
                            byte[] compressedBuffer = new byte[bufferSize];
                            string compressedBufferString = "";
                            do // START OF WHILE: (leftBlockNumber != nextLeftBlockNumber) || (rightBlockNumber != nextRightBlockNumber) && inFile.Position < inFile.Length 
                            {  // => While not found and not end of file
                                // For save buffer length (not using more and more memory)
                                if (compressedBufferString != "")                       // second loop
                                {
                                    if (secondIndex == NOT_FOUND)
                                    {   // need to expand memory adding new data
                                        // read new portion of data and add to string
                                        readCompressedBytes = inFile.Read(compressedBuffer, 0, compressedBuffer.Length);
                                        compressedBufferString += Encoding.Default.GetString(compressedBuffer);
                                        compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);                   // save buffer to memory
                                    }
                                    else
                                    {   // read to next block
                                        compressedBufferString = compressedBufferString.Remove(0, secondIndex - 1);         // remove [1block] from string. Save [2block+] data
                                        var compMemBuff = compressedMemory.GetBuffer();
                                        // clear compressed memory
                                        Array.Clear(compMemBuff, 0, compMemBuff.Length);
                                        compressedMemory.SetLength(0);
                                        compressedMemory.Position = 0;
                                        compressedMemory.Capacity = 0;
                                        compressedMemory.Flush();
                                        // write to compressed memory remainded block
                                        var bufferRemainder = Encoding.Default.GetBytes(compressedBufferString);
                                        compressedMemory.Write(bufferRemainder, 0, bufferRemainder.Length);
                                        // change indexes and numbers (set Right -> Left)
                                        firstIndex = secondIndex;
                                        leftBlockNumber = rightBlockNumber;
                                        // read new portion of data and add to string
                                        readCompressedBytes = inFile.Read(compressedBuffer, 0, compressedBuffer.Length);
                                        compressedBufferString += Encoding.Default.GetString(compressedBuffer);
                                        compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);                   // save buffer to memory

                                    }
                                }
                                else // first loop
                                {
                                    readCompressedBytes = inFile.Read(compressedBuffer, 0, compressedBuffer.Length);        // read to buffer
                                    compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);                       // save buffer to memory
                                    compressedBufferString = Encoding.Default.GetString(compressedBuffer);                  // get string from buffer for search
                                    firstIndex = compressedBufferString.IndexOf(COMPRESSED_BLOCK_START);                    // get start of compressed block (magic number)
                                    while (firstIndex == NOT_FOUND && inFile.Position < inFile.Length)
                                    {
                                        //Array.Resize(ref compressedBuffer, compressedBuffer.Length * 2);
                                        //byte[] newBuffer = new byte[bufferSize];
                                        //readCompressedBytes = inFile.Read(newBuffer, 0, newBuffer.Length);
                                        readCompressedBytes = inFile.Read(compressedBuffer, 0, compressedBuffer.Length);    // read new data to buffer
                                        compressedBufferString += Encoding.Default.GetString(compressedBuffer);             // add second part to first
                                        firstIndex = compressedBufferString.IndexOf(COMPRESSED_BLOCK_START);
                                        compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);                   // save buffer to memory
                                    }; // END OF WHILE
                                    if (firstIndex != NOT_FOUND) // For multithread
                                    {
                                        leftBlockNumber++;
                                    }
                                }
                                secondIndex = compressedBufferString.IndexOf(COMPRESSED_BLOCK_START, firstIndex + 1);       // get start of next compressed block (magic number)
                                while (secondIndex == NOT_FOUND && inFile.Position < inFile.Length)
                                {
                                    //compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);
                                    readCompressedBytes = inFile.Read(compressedBuffer, 0, compressedBuffer.Length);
                                    compressedBufferString += Encoding.Default.GetString(compressedBuffer);                 // add second part to first
                                    secondIndex = compressedBufferString.IndexOf(COMPRESSED_BLOCK_START, firstIndex + 1);
                                    compressedMemory.Write(compressedBuffer, 0, readCompressedBytes);
                                }; // END OF WHILE
                                if ((secondIndex != NOT_FOUND) && (inFile.Position < inFile.Length)) // For multithread
                                {
                                    rightBlockNumber++;
                                    // inFile -> second block length
                                    //inFile.Position = inFile.Position - (readCompressedBytes - secondIndex);              // roll back inFile stream to start of second block 
                                    //compressedBufferString.Length
                                }
                                //else // not found => end of file
                                //{
                                //    //inFile.Position = inFile.Position - (compressedBufferString.Length - firstIndex);   // roll back inFile stream to start of second block
                                //}
                            } // END OF WHILE
                            // While not found and not end of file
                            while (((leftBlockNumber != nextLeftBlockNumber) || (rightBlockNumber != nextRightBlockNumber)) && (inFile.Position < inFile.Length));
                            if (((leftBlockNumber == nextLeftBlockNumber) && (rightBlockNumber == nextRightBlockNumber)) || (inFile.Position == inFile.Length))
                            {
                                // success => cut the block and put in memory
                                var compMemBuff = compressedMemory.GetBuffer();
                                // clear compressed memory
                                Array.Clear(compMemBuff, 0, compMemBuff.Length);
                                compressedMemory.SetLength(0);
                                compressedMemory.Position = 0;
                                compressedMemory.Capacity = 0;
                                compressedMemory.Flush();
                                // write to compressed memory remainded block
                                if (secondIndex != NOT_FOUND)
                                {
                                    // remove all after [2block] from string
                                    compressedBufferString = compressedBufferString.Remove(secondIndex, compressedBufferString.Length - secondIndex);
                                    if (inFile.Position > (readCompressedBytes - secondIndex + 1))
                                    {
                                        inFile.Position = inFile.Position - (readCompressedBytes - secondIndex + 1);                    // roll back inFile stream to start of second block
                                    }
                                }
                                compressedBufferString = compressedBufferString.Remove(0, firstIndex);                                  // remove all before [1block] from string
                                var bufferRemainder = Encoding.Default.GetBytes(compressedBufferString);
                                compressedMemory.Write(bufferRemainder, 0, bufferRemainder.Length);
                            }
                            // Compressed block found
                            using (MemoryStream decompressedMemory = new MemoryStream())
                            {
                                compressedMemory.Position = 0;
                                //using (GZipStream gzip = new GZipStream(inFile, CompressionMode.Decompress, true))
                                using (GZipStream gzip = new GZipStream(compressedMemory, CompressionMode.Decompress, true))
                                {
                                    gzip.CopyTo(decompressedMemory);
                                } // END OF USING GZipStream (gzip)
                                #region COMMENTED
                                /*
                                    int readBytes = 0;
                                    long threadProcessedSize = 0;
                                    byte[] buffer = new byte[bufferSize];
                                    //gzip.CopyTo(compressedMemory, bufferSize);
                                    //gzip.Read(buffer, 0, buffer.Length);
                                    readBytes = gzip.Read(buffer, 0, buffer.Length);
                                    if (readBytes == 0)
                                    {
                                        break;
                                    }
                                    byte[] readBuffer = new byte[readBytes];
                                    Array.Copy(buffer, 0, readBuffer, 0, readBytes);                // Copy 64 Kb array to array with length = read bytes
                                    memory.Write(readBuffer, 0, readBytes);
                                    */
                                #endregion
                                // Create task: enqueue compressed bytes using compressedMemory.ToArray()
                                // Try until task be enqueued
                                bool isSuccess = false;
                                do
                                {
                                    isSuccess = writer.EnqueueTask(decompressedMemory.ToArray(), parameters.tid);
                                } while (!isSuccess);
                                #region COMMENTED
                                // Reset compressedMemory stream
                                //byte[] memoryBuffer = decompressedMemory.GetBuffer();
                                //Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                //decompressedMemory.Position = 0;
                                //decompressedMemory.SetLength(0);
                                //decompressedMemory.Capacity = 0;
                                //decompressedMemory.Flush();
                                #endregion
                                threadProcessedSize += readCompressedBytes; // readBytes;                   // Calculate result compressed size by thread
                                if (threadProcessedSize > inFile.Length)
                                {
                                    threadProcessedSize = inFile.Length;
                                }
                                #region Offset
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
                                #endregion
                                // Seek for new position
                                //inFile.Seek((optimalThreadCount - 2) * bufferSize,                        // Offset for thread of read file,
                                //    SeekOrigin.Current);                                                  // 1 thread for write -> (tCount - 2)
                                inFile.Position = inFile.Position + (optimalThreadCount - 2) * bufferSize;

                                if (parameters.showMessagesInConsole)
                                {
                                    // Console info:
                                    lock (threadConsoleLock)
                                    {
                                        System.Console.CursorTop = parameters.consoleCursorTop;
                                        System.Console.CursorLeft = 0;
                                        System.Console.Write(" - {0}: Completed {1}%, {2:N}/{3:N} bytes",   // Thread #1: Completed 0%, 0/10 000.00 bytes
                                            Thread.CurrentThread.Name,
                                            (threadProcessedSize * 100) / (inFile.Length / threadCount),    // % of compressed by thread devided to size
                                            threadProcessedSize,                                            // that thread must compress
                                            inFile.Length / threadCount);
                                    }
                                }
                            } // END OF USING MemoryStream (decompressedMemory)
                        } // END OF USING MemoryStream (compressedMemory)
                    } // END OF WHILE
                    while (inFile.Position < inFile.Length);
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
