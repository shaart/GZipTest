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
        /// <summary>
        /// 128K bytes
        /// </summary>
        const int bufferSize = 128 * 1024;
        /// <summary>
        /// 32K bytes
        /// </summary>
        const int bufferSizeSmall = 32 * 1024;
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
            //Compress(@"d:\dump\debug_original.txt", @"d:\dump\debug_compressed128.txt.gz");
            //Compress(@"d:\dump\debug_original_big.txt", @"d:\dump\debug_compressed_big.txt.gz");
            //Compress(@"d:\dump\uita.mp3", @"d:\dump\uita_comp_17-07-2016.mp3.gz");            // Testing for decompress using 7z => Works. Compressing is correct
            //FileInfo inFileInfo = new FileInfo(@"d:\dump\debug_original.txt");
            //GZip.DefaultCompress(inFileInfo);

            // Работает так же, как и decompress()
            //Decompress(@"d:\dump\debug_compressed.txt.gz", @"d:\dump\debug_decompressed.txt");
            //Decompress(@"d:\dump\debug_compressed64.txt.gz", @"d:\dump\debug_decompressed64.txt");
            //Decompress(@"d:\dump\debug_compressed_big.txt.gz", @"d:\dump\debug_decompressed_big.txt");
            Decompress(@"d:\dump\uita_comp.gz", @"d:\dump\uita_decomp.mp3");
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
                1,                                  // thread count
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
                                #region COMMENTED
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
                                #endregion
                                gzip.Write(buffer, 0, readBytes);

                                dump = Encoding.Default.GetString(buffer);
                                #region COMMENTED
                                /*
                                lock (threadConsoleLock)
                                {
                                    string stringInBuffer = Encoding.Default.GetString(readBuffer);
                                    System.Console.Write("String sended to writer: " + stringInBuffer);
                                }
                                */
                                #endregion
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
                            #region COMMENTED
                            /*
                            var memoryBuffer = compressedMemory.GetBuffer();                                                  // Проблема появляется тут
                            string dumpMem = Encoding.Default.GetString(memoryBuffer);
                            if (memoryBuffer.Length != compressedMemory.Length)
                            {
                                byte[] memoryBufferToWrite = new byte[compressedMemory.Length];
                                Array.Copy(memoryBuffer, 0, memoryBufferToWrite, 0, compressedMemory.Length);
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
                            #endregion
                            // Clear compressedMemory stream
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
            // String.IndexOf search result: -1
            const int NOT_FOUND = -1;
            const string COMPRESSED_BLOCK_START = "‹\b\0\0\0\0\0\0";
            // Create new WriterQueue and start his thread
            var writer = new DebugWriterQueue(
                resultFilePath,  // result file path 
                1,                                  // thread cound
                currentCursorTop + 1,               // console cursor top 
                true,                               // show message in console 
                threadConsoleLock);                 // console lock
            using (File.Create(@"d:\dump\debug_decompress_log.txt"))
            { }

            try
            {
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
                    int nextLeftBlockNumber = -1;   // For multithread
                    int nextRightBlockNumber = 0;   // For multithread
                    int leftBlockNumber = -1;
                    int rightBlockNumber = 0;
                    int firstIndex = NOT_FOUND;
                    int secondIndex = NOT_FOUND;
                    do
                    { // START OF MAIN WHILE: inFile.Position < inFile.Length
                        using (MemoryStream compressedMemory = new MemoryStream())
                        {
                            firstIndex = NOT_FOUND;
                            secondIndex = NOT_FOUND;
                            int readCompressedBytes = 0;
                            nextLeftBlockNumber++;  // first loop: 0 => at multithread change to threads offset (threadNum)
                            nextRightBlockNumber++; // first loop: 1 => see line above ^
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
                                        compressedBufferString = compressedBufferString.Remove(0, secondIndex - 1);  // remove [1block] from string. Save [2block+] data
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
                                    //inFile.Position = inFile.Position - (readCompressedBytes - secondIndex);      // roll back inFile stream to start of second block 
                                                                        //compressedBufferString.Length
                                }
                                //else // not found => end of file
                                //{
                                //    //inFile.Position = inFile.Position - (compressedBufferString.Length - firstIndex);      // roll back inFile stream to start of second block
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
                                compressedBufferString = compressedBufferString.Remove(0, firstIndex);                                                   // remove all before [1block] from string
                                var bufferRemainder = Encoding.Default.GetBytes(compressedBufferString);
                                compressedMemory.Write(bufferRemainder, 0, bufferRemainder.Length);
                            }
                            // Compressed block found
                            using (MemoryStream decompressedMemory = new MemoryStream())
                            {
                                compressedMemory.Position = 0;
                                using (GZipStream gzip = new GZipStream(compressedMemory, CompressionMode.Decompress, true))
                                //using (GZipStream gzip = new GZipStream(inFile, CompressionMode.Decompress, true))
                                //using (GZipStream gzip = new GZipStream(new MemoryStream(buffer), CompressionMode.Decompress, true))
                                {
                                    gzip.CopyTo(decompressedMemory);

                                    #region COMMENTED
                                    //int readBytes = 0;
                                    //byte[] buffer = new byte[bufferSize];
                                    //int readBytesGzip = 0;
                                    //byte[] decompressedBuffer = new byte[bufferSizeSmall];

                                    //do
                                    //{ // START OF while ((readBytes > 0) && (inFile.Position < inFile.Length));

                                    //    readBytes = inFile.Read(buffer, 0, buffer.Length);      // Считали сжатые данные из файла. 4Кб
                                    //    string dumpBuff = Encoding.Default.GetString(buffer);
                                    //    if (readBytes == 0) { break; }
                                    //    compressedMemory.Write(buffer, 0, readBytes);                     // Записали в MemoryStream, хранящий сжатые данные, считанные из файла
                                    //    var memArray = compressedMemory.ToArray();
                                    //    dumpBuff = Encoding.Default.GetString(buffer);
                                    //    string dumpMem = Encoding.Default.GetString(memArray);

                                    //    compressedMemory.Position = 0;
                                    //    //do
                                    //    //{
                                    //    //readBytes = gzip.Read(result, 0, result.Length);
                                    //    //gzip.CopyTo(decompressedMemory);
                                    //    do
                                    //    { // START OF while (readBytesGzip > 0);
                                    //        readBytesGzip = gzip.Read(decompressedBuffer, 0, decompressedBuffer.Length);
                                    //        if (readBytesGzip > 0)
                                    //        {
                                    //            decompressedMemory.Write(decompressedBuffer, 0, readBytesGzip); // Записали в итоговую память
                                    //        }
                                    //    } // END OF WHILE
                                    //    while (readBytesGzip > 0);

                                    //    #region COMMENTED
                                    //    /*
                                    //if (readBytes != buffer.Length)
                                    //{
                                    //    byte[] readBuffer = new byte[readBytes];
                                    //    Array.Copy(buffer, 0, readBuffer, 0, readBytes);
                                    //    dumpBuff = Encoding.Default.GetString(readBuffer);
                                    //    writer.EnqueueTask(
                                    //        readBuffer,    // task 
                                    //        0);            // thread local id
                                    //    using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //    {
                                    //        debugLog.Seek(0, SeekOrigin.End);
                                    //        debugLog.Write(readBuffer, 0, readBuffer.Length);
                                    //    }
                                    //}
                                    //else
                                    //{
                                    //    writer.EnqueueTask(
                                    //        buffer,       // task 
                                    //        0);           // thread local id
                                    //    using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //    {
                                    //        debugLog.Seek(0, SeekOrigin.End);
                                    //        debugLog.Write(buffer, 0, buffer.Length);
                                    //    }
                                    //}
                                    //*/
                                    //    #endregion
                                    //    //int readBytesGzip = 0;
                                    //    //byte[] bufferGzip = new byte[bufferSize];
                                    //    //do
                                    //    //{
                                    //    //    readBytesGzip = gzip.Read(bufferGzip, 0, bufferGzip.Length);    // Расжали данные в новый буфер
                                    //    //    dumpBuff = Encoding.Default.GetString(bufferGzip);
                                    //    //    if (readBytesGzip > 0)
                                    //    //    {
                                    //    //        //compressedMemory.Write(bufferGzip, 0, readBytesGzip);                 // Записали в итоговую память
                                    //    //        using (FileStream outStream = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //    //        {
                                    //    //            outStream.Seek(0, SeekOrigin.End);
                                    //    //            outStream.Write(bufferGzip, 0, readBytesGzip);
                                    //    //        }
                                    //    //    }
                                    //    //}
                                    //    //while (readBytesGzip > 0);
                                    //    #region COMMENTED
                                    //    //gzip.Flush();
                                    //    /*
                                    //    if (readBytes != buffer.Length)
                                    //    {
                                    //        byte[] readBuffer = new byte[readBytes];
                                    //        Array.Copy(buffer, 0, readBuffer, 0, readBytes);
                                    //        compressedMemory.Write(readBuffer, 0, readBytes);
                                    //        using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //        {
                                    //            debugLog.Seek(0, SeekOrigin.End);
                                    //            debugLog.Write(readBuffer, 0, readBuffer.Length);
                                    //        }
                                    //    }
                                    //    else
                                    //    {
                                    //        compressedMemory.Write(buffer, 0, readBytes);
                                    //        using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //        {
                                    //            debugLog.Seek(0, SeekOrigin.End);
                                    //            debugLog.Write(buffer, 0, buffer.Length);
                                    //        }
                                    //    }
                                    //    var memoryBuffer = compressedMemory.GetBuffer();
                                    //    if (memoryBuffer.Length != compressedMemory.Length)
                                    //    {
                                    //        byte[] memoryBufferToWrite = new byte[compressedMemory.Length];
                                    //        Array.Copy(memoryBuffer, 0, memoryBufferToWrite, 0, compressedMemory.Length);
                                    //        writer.EnqueueTask(
                                    //            memoryBufferToWrite,    // task 
                                    //            0);                     // thread local id

                                    //        using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //        {
                                    //            debugLog.Seek(0, SeekOrigin.End);
                                    //            debugLog.Write(memoryBufferToWrite, 0, memoryBufferToWrite.Length);
                                    //        }
                                    //    }
                                    //    else
                                    //    {
                                    //        writer.EnqueueTask(
                                    //            memoryBuffer,           // task 
                                    //            0);                     // thread local id

                                    //        using (FileStream debugLog = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //        {
                                    //            debugLog.Seek(0, SeekOrigin.End);
                                    //            debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                                    //        }
                                    //    }

                                    //    // Clear compressedMemory stream
                                    //    Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                    //    compressedMemory.SetLength(0);
                                    //    compressedMemory.Position = 0;
                                    //    compressedMemory.Capacity = 0;
                                    //    compressedMemory.Flush();
                                    //    */
                                    //    //while (inFile.Position < inFile.Length);

                                    //    //var memoryBuffer = compressedMemory.ToArray();
                                    //    //using (FileStream debugLog = File.Create(@"d:\dump\debug_decompress_log_memory.txt"))
                                    //    //{
                                    //    //    debugLog.Seek(0, SeekOrigin.End);
                                    //    //    debugLog.Write(memoryBuffer, 0, memoryBuffer.Length);
                                    //    //}
                                    //    #endregion

                                    //    // Clear compressedMemory stream
                                    //    var memoryBuffer = compressedMemory.GetBuffer();
                                    //    Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                    //    compressedMemory.SetLength(0);
                                    //    compressedMemory.Position = 0;
                                    //    compressedMemory.Capacity = 0;
                                    //    compressedMemory.Flush();

                                    //    if (decompressedMemory.Length > 0)
                                    //    {
                                    //        var decompMemArray = decompressedMemory.ToArray();
                                    //        string dumpDecompMem = Encoding.Default.GetString(decompMemArray);
                                    //        using (FileStream outStream = File.OpenWrite(@"d:\dump\debug_decompress_log.txt"))
                                    //        {
                                    //            outStream.Seek(0, SeekOrigin.End);
                                    //            outStream.Write(decompMemArray, 0, decompMemArray.Length);
                                    //        }
                                    //        writer.EnqueueTask(
                                    //            decompMemArray,
                                    //            0);
                                    //        //}
                                    //        //while (readBytesGzip > 0);
                                    //        //while (compressedMemory.Position < compressedMemory.Length);

                                    //        // Сжатые данные, считанные в память, закончились. Очищаем и читаем дальше
                                    //        // Clear decompressedMemory stream
                                    //        memoryBuffer = decompressedMemory.GetBuffer();
                                    //        Array.Clear(memoryBuffer, 0, memoryBuffer.Length);
                                    //        decompressedMemory.SetLength(0);
                                    //        decompressedMemory.Position = 0;
                                    //        decompressedMemory.Capacity = 0;
                                    //        decompressedMemory.Flush();
                                    //    }
                                    //} // END OF WHILE
                                    //while ((readBytes > 0) && (inFile.Position < inFile.Length));
                                    #endregion

                                } // END OF USING GZipStream (gzip)
                                var memoryBuffer = decompressedMemory.ToArray();
                                var memStr = Encoding.Default.GetString(memoryBuffer);
                                writer.EnqueueTask(
                                    memoryBuffer,
                                    0);
                            } // END OF USING MemoryStream (decompressedMemory)
                        } // END OF USING MemoryStream (compressedMemory)
                        //inFile.Position = 1dasd;
                    } // END OF WHILE
                    while (inFile.Position < inFile.Length);
                } // END OF USING FileStream (inFile)
            }
            //catch (Exception ex)
            //{
            //    throw ex;
            //}
            finally
            {
                writer.Dispose();
            }
        }
    }
}
