using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.IO;                        // File, FileInfo, FileStream
using System.IO.Compression;            // GZipStream
using System.Threading;                 // Thread

namespace GZipTest
{
    class DebugClass
    {
        //const int bufferSize = 64 * 1024;
        /// <summary>
        /// 32K bytes
        /// </summary>
        const int bufferSize = 32 * 1024;
        static object threadConsoleLock = new object();
        static int currentCursorTop = Console.CursorTop;             // Save console cursor row position


        public static void Start()
        {
            #region Block for manual debugging
            /*
            string f1 = @"D:\dump\uita.mp3";
            // Check files existance
            FileInfo fi01 = new FileInfo(f1);
            if (!fi01.Exists)
            {
                System.Console.WriteLine("Error: file \"{0}\" not found.", f1);
            }
            GZip.DefaultCompress(fi01);
            string f2 = @"D:\dump\uita.mp3.gz";
            // Check files existance
            FileInfo fi02 = new FileInfo(f2);
            if (!fi02.Exists)
            {
                System.Console.WriteLine("Error: file \"{0}\" not found.", f1);
            }
            GZip.DefaultDecompress(fi02);

            return 0;
            */
            /*
            string f1 = @"D:\dump\1.txt";
            string f2 = @"D:\dump\2.txt";
            //string f2 = @"D:\dump\2.txt.gz";
            string f3 = @"D:\dump\3.txt";
            //string f1 = @"D:\dump\1.jpg";
            //string f2 = @"D:\dump\2.jpg.gz";
            //string f3 = @"D:\dump\3.jpg";
            //string f1 = @"D:\dump\uita.mp3";
            //string f2 = @"D:\dump\uita_1.mp3.gz";
            //string f3 = @"D:\dump\uita_2.mp3";
            //string f1 = @"D:\dump\fmmc2.mp3.gz";
            //string f2 = @"D:\dump\fmmc3.mp3";
            //string f1 = @"D:\dump\fmmc.mp3";
            //string f2 = @"D:\dump\fmmc2.mp3.gz";
            // Check files existance
            FileInfo fi01 = new FileInfo(f1);
            if (!fi01.Exists)
            {
                System.Console.WriteLine("Error: file \"{0}\" not found.", f1);
            }
            try
            {
                long archiveLength = GZip.Compress(fi01, f2);
                //FileInfo fi02 = new FileInfo(f2);
                //long archiveLength = GZip.Decompress(fi02, f3);
            }
            catch (Exception ex)
            {
                System.Console.WriteLine("Error: {0}", ex.Message);
            }
            return 0;
            */
            #endregion

            // Не до конца записывает сжатый файл в отличие от DefaultCompress
            Compress(@"d:\dump\debug_original.txt", @"d:\dump\debug_compressed.txt");
            //FileInfo inFileInfo = new FileInfo(@"d:\dump\debug_original.txt");
            //GZip.DefaultCompress(inFileInfo);

            // Работает так же, как и decompress()
            Decompress(@"d:\dump\debug_compressed.txt", @"d:\dump\debug_decompressed.txt");
            //Decompress(@"d:\dump\debug_original_defaultcompress.txt", @"d:\dump\debug_decompressed.txt");
            //FileInfo inFileInfo = new FileInfo(@"d:\dump\debug_compressed.txt");
            //GZip.DefaultDecompress(inFileInfo);

            lock (threadConsoleLock)
            {
                System.Console.WriteLine("\n\nEnd. Press any key to exit...");
            }
            Console.ReadKey();
        }

        private static void Compress(string originalFilePath = @"d:\dump\debug_original.txt", string resultFilePath = @"d:\dump\debug_compressed.txt")
        {
            // Файл, в который записывается буфер считанных данных и следом за них - они же в сжатом виде
            const string DEBUG_COMPRESS_LOG = @"d:\dump\debug_compress_log.txt";
            // Файл, в который записывается только буфер сжатых данных
            const string DEBUG_COMPRESS_LOG_DEBUG = @"d:\dump\debug_compress_in_debug.txt";

            // Create new WriterQueue and start his thread
            var writer = new DebugWriterQueue(
                resultFilePath,                     // result file path 
                1,                                  // thread cound
                currentCursorTop + 1,               // console cursor top 
                true,                               // show message in console 
                threadConsoleLock);                 // console lock
            using (File.Create(DEBUG_COMPRESS_LOG))
            { }
            using (File.Create(DEBUG_COMPRESS_LOG_DEBUG))
            { }

            try
            {
                using (var inFile = File.OpenRead(originalFilePath))
                {
                    // Memory stream as buffer
                    using (MemoryStream memory = new MemoryStream())
                    {
                        int readBytes = 0;
                        byte[] buffer = new byte[bufferSize];
                        do
                        {
                            readBytes = inFile.Read(buffer, 0, buffer.Length);
                            if (readBytes == 0) { break; }
                            string dump = "";
                            string dump2 = "";
                            using (GZipStream gzip = new GZipStream(memory, CompressionMode.Compress, true))
                            {
                                /*
                                if (readBytes != buffer.Length)
                                {
                                    byte[] readBuffer = new byte[readBytes];
                                    Array.Copy(buffer, 0, readBuffer, 0, readBytes);
                                    dump2 = Encoding.Default.GetString(readBuffer);
                                    gzip.Write(readBuffer, 0, readBytes);                                               // Проблема появляется тут
                                    using (FileStream debugLog = File.OpenWrite(DEBUG_COMPRESS_LOG))
                                    {
                                        debugLog.Seek(0, SeekOrigin.End);
                                        debugLog.Write(readBuffer, 0, readBuffer.Length);
                                    }
                                }
                                else
                                {
                                    gzip.Write(buffer, 0, readBytes);                                                   // Проблема появляется тут
                                    using (FileStream debugLog = File.OpenWrite(DEBUG_COMPRESS_LOG))
                                    {
                                        debugLog.Seek(0, SeekOrigin.End);
                                        debugLog.Write(buffer, 0, buffer.Length);
                                    }
                                }
                                */
                                gzip.Write(buffer, 0, readBytes);

                                dump = Encoding.Default.GetString(buffer);

                                /*
                                lock (threadConsoleLock)
                                {
                                    string stringInBuffer = Encoding.Default.GetString(readBuffer);
                                    System.Console.Write("String sended to writer: " + stringInBuffer);
                                }
                                */

                            } // END OF USING GZipStream

                            if (memory.Length == 0)
                            {
                                throw new Exception("Data did not compressed");
                            }
                            using (FileStream debugLog = File.OpenWrite(DEBUG_COMPRESS_LOG))
                            {
                                debugLog.Seek(0, SeekOrigin.End);
                                debugLog.Write(buffer, 0, readBytes);
                            }
                            var memoryBuffer = memory.ToArray();  // Считываем массив данных из буфера памяти (пустые данные не считываются в отличие от GetBuffer())
                            string dumpMem1 = Encoding.Default.GetString(memoryBuffer);

                            writer.EnqueueTask(
                                memoryBuffer,           // task 
                                0);                     // thread local id
                            using (FileStream debugLog = File.OpenWrite(DEBUG_COMPRESS_LOG))
                            {
                                debugLog.Seek(0, SeekOrigin.End);
                                string stringInBuffer = Encoding.Default.GetString(memoryBuffer);
                                debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                            }
                            using (FileStream outStream = File.OpenWrite(DEBUG_COMPRESS_LOG_DEBUG))
                            {
                                outStream.Seek(0, SeekOrigin.End);
                                outStream.Write(memoryBuffer, 0, memoryBuffer.Length);
                            }

                            /*
                            var memoryBuffer = memory.GetBuffer();                                                  // Проблема появляется тут
                            string dumpMem = Encoding.Default.GetString(memoryBuffer);
                            if (memoryBuffer.Length != memory.Length)
                            {
                                byte[] memoryBufferToWrite = new byte[memory.Length];
                                Array.Copy(memoryBuffer, 0, memoryBufferToWrite, 0, memory.Length);
                                writer.EnqueueTask(
                                    memoryBufferToWrite,    // task 
                                    0);                     // thread local id
                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_compress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    debugLog.Write(memoryBufferToWrite, 0, memoryBufferToWrite.Length);
                                }
                                using (FileStream outStream = File.OpenWrite(@"d:\dump\debug_compress_in_debug.txt"))
                                {
                                    outStream.Seek(0, SeekOrigin.End);
                                    outStream.Write(memoryBufferToWrite, 0, memoryBufferToWrite.Length);
                                }
                            }
                            else
                            { 
                                writer.EnqueueTask(
                                    memoryBuffer,           // task 
                                    0);                     // thread local id
                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_compress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    string stringInBuffer = Encoding.Default.GetString(memoryBuffer);
                                    debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                                }
                                using (FileStream outStream = File.OpenWrite(@"d:\dump\debug_compress_in_debug.txt"))
                                {
                                    outStream.Seek(0, SeekOrigin.End);
                                    outStream.Write(memoryBuffer, 0, memoryBuffer.Length);
                                }
                            }
                            */
                            // Clear memory stream
                            Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                            memory.SetLength(0);
                            memory.Position = 0;
                            memory.Capacity = 0;
                            memory.Flush();
                            //}


                        } // END OF WHILE
                        while (readBytes > 0);
                    } // END OF USING MemoryStream
                } // END OF USING FileStream

                /*
                for (int i = 0; i < 10000; i++)
                {
                    writer.EnqueueTask(
                        BitConverter.GetBytes(i),   // task 
                        0);                         // thread local id
                }
                */
                writer.Dispose();
                writer.WorkerThread.Join();
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        private static void Decompress(string originalFilePath = @"d:\dump\debug_compressed.txt", string resultFilePath = @"d:\dump\debug_decompressed.txt")
        {
            // Create new WriterQueue and start his thread
            var writer = new DebugWriterQueue(
                resultFilePath,  // result file path 
                1,                                  // thread cound
                currentCursorTop + 1,               // console cursor top 
                true,                               // show message in console 
                threadConsoleLock);                 // console lock
            using (File.Create(@"d:\dump\debug_decompress_log.txt"))
            { }

            using (var inFile = File.OpenRead(originalFilePath))
            {
                //
                // Проблема:
                //      Расжатие файла останавливается на определенном моменте, а gzip сообщает, что всё расжал
                //
                // Возможное решение:
                //      Самому считывать файл кусками (буфер), а те уже расжимать и писать в конечный файл
                //

                // Обновлено
                // Проблема:
                //      Gzip сообщает, что магическое число ...
                // (Если правильно понял!) Возникает из-за того, что файл теперь читается частями и "магические числа" не всегда попадают в начало считываемого блока
                //
                // Возможное решение:
                //      Считывать блок до следующего "магического числа"
                //
                // Memory stream as buffer
                using (MemoryStream memory = new MemoryStream())
                {
                    int readBytes = 0;
                    byte[] buffer = new byte[bufferSize];
                    do
                    {
                        readBytes = inFile.Read(buffer, 0, buffer.Length);      // Считали сжатые данные из файла
                        string dumpBuff = Encoding.Default.GetString(buffer);
                        if (readBytes == 0) { break; }
                        memory.Write(buffer, 0, readBytes);                     // Записали в MemoryStream, хранящий сжатые данные, считанные из файла
                        //using (GZipStream gzip = new GZipStream(memory, CompressionMode.Decompress, true))
                        using (GZipStream gzip = new GZipStream(new MemoryStream(buffer), CompressionMode.Decompress, true))
                        {
                            //readBytes = gzip.Read(buffer, 0, buffer.Length);
                            var memArray = memory.ToArray();
                            dumpBuff = Encoding.Default.GetString(buffer);
                            string dumpMem = Encoding.Default.GetString(memArray);
                            #region COMMENTED
                            /*
                                if (readBytes != buffer.Length)
                                {
                                    byte[] readBuffer = new byte[readBytes];
                                    Array.Copy(buffer, 0, readBuffer, 0, readBytes);
                                    dumpBuff = Encoding.Default.GetString(readBuffer);
                                    writer.EnqueueTask(
                                        readBuffer,    // task 
                                        0);            // thread local id
                                    using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    {
                                        debugLog.Seek(0, SeekOrigin.End);
                                        debugLog.Write(readBuffer, 0, readBuffer.Length);
                                    }
                                }
                                else
                                {
                                    writer.EnqueueTask(
                                        buffer,       // task 
                                        0);           // thread local id
                                    using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    {
                                        debugLog.Seek(0, SeekOrigin.End);
                                        debugLog.Write(buffer, 0, buffer.Length);
                                    }
                                }
                                */
                            #endregion
                            int readBytesGzip = 0;
                            byte[] bufferGzip = new byte[bufferSize];
                            do
                            {
                                readBytesGzip = gzip.Read(bufferGzip, 0, bufferGzip.Length);    // Расжали данные в новый буфер
                                dumpBuff = Encoding.Default.GetString(bufferGzip);
                                if (readBytesGzip > 0)
                                {
                                    //memory.Write(bufferGzip, 0, readBytesGzip);                 // Записали в итоговую память
                                    using (FileStream outStream = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    {
                                        outStream.Seek(0, SeekOrigin.End);
                                        outStream.Write(bufferGzip, 0, readBytesGzip);
                                    }
                                }
                            }
                            while (readBytesGzip > 0);
                            #region COMMENTED
                            //gzip.Flush();
                            /*
                            if (readBytes != buffer.Length)
                            {
                                byte[] readBuffer = new byte[readBytes];
                                Array.Copy(buffer, 0, readBuffer, 0, readBytes);
                                memory.Write(readBuffer, 0, readBytes);
                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    debugLog.Write(readBuffer, 0, readBuffer.Length);
                                }
                            }
                            else
                            {
                                memory.Write(buffer, 0, readBytes);
                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    debugLog.Write(buffer, 0, buffer.Length);
                                }
                            }
                            var memoryBuffer = memory.GetBuffer();
                            if (memoryBuffer.Length != memory.Length)
                            {
                                byte[] memoryBufferToWrite = new byte[memory.Length];
                                Array.Copy(memoryBuffer, 0, memoryBufferToWrite, 0, memory.Length);
                                writer.EnqueueTask(
                                    memoryBufferToWrite,    // task 
                                    0);                     // thread local id

                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    debugLog.Write(memoryBufferToWrite, 0, memoryBufferToWrite.Length);
                                }
                            }
                            else
                            {
                                writer.EnqueueTask(
                                    memoryBuffer,           // task 
                                    0);                     // thread local id

                                using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                {
                                    debugLog.Seek(0, SeekOrigin.End);
                                    debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                                }
                            }

                            // Clear memory stream
                            Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                            memory.SetLength(0);
                            memory.Position = 0;
                            memory.Capacity = 0;
                            memory.Flush();
                            */
                            //while (inFile.Position < inFile.Length);

                            //var memoryBuffer = memory.ToArray();
                            //using (FileStream debugLog = File.Create(@"d:\dump\debug_decompress_log_memory.txt"))
                            //{
                            //    debugLog.Seek(0, SeekOrigin.End);
                            //    debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                            //}
                            #endregion
                        } // END OF USING GZipStream (gzip)
                        // Сжатые данные, считанные в память, закончились. Очищаем и читаем дальше
                        // Clear memory stream
                        //var memoryBuffer = memory.GetBuffer();
                        //Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                        //memory.SetLength(0);
                        //memory.Position = 0;
                        //memory.Capacity = 0;
                        //memory.Flush();
                    } // END OF WHILE
                    while (readBytes > 0);

                } // END OF USING MemoryStream (memory)
            } // END OF USING FileStream (inFile)
            writer.Dispose();
        }
    }
}
