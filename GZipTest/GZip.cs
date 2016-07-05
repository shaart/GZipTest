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

// Как каждый поток узнает, откуда ему читать и писать?
// Как узнать прогресс выполнения задачи? Как писать его в консоль? Cursor

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
 */
namespace GZipTest
{
    class GZip
    {
        /// <summary>
        /// Buffer for GZip: 64 KB
        /// </summary>
        const int bufferSize = 64 * 1024;
        private long compressedSize = 0;
        public long CompressedSize { get { return compressedSize; } }
        static int threadCount = 0;

        /// <summary>
        /// Compress file to archive
        /// </summary>
        /// <param name="fi">FileInfo of original file</param>
        /// <param name="archivePath">Path to resulting archive</param>
        public static long Compress(FileInfo fi, string archivePath)
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
                        System.Console.WriteLine("Started compressing file \"{0}\"...", fi.Name);
                        // Create the compressed file.

                        // For multi-threading: optimal count of thread. 
                        // User can edit this value at system variables
                        int processorCount = System.Environment.ProcessorCount;
                        threadCount = 0;

                        using (FileStream outFile = File.Create(archivePath))
                        {
                            /*
                            using (GZipStream Compress = new GZipStream(outFile, CompressionMode.Compress))
                            {
                                // Copy the source file into the compression stream.
                                inFile.CopyTo(Compress);                                

                            }
                             */
                            for (int i = 0; i < processorCount; i++)
                            {
                                Thread thread = new Thread(CompressThreadFunction);
                                thread.Start(); //fi, archivePath, i * bufferSize + 1, i * bufferSize + 1, bufferSize, bufferSize
                            }
                            
                            System.Console.WriteLine("Compressed {0} from {1} to {2} bytes.",
                                fi.Name,
                                fi.Length.ToString(),
                                outFile.Length.ToString());
                            return outFile.Length;                 
                        }
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
        }
        // offset - смещение байт в потоке
        // Метод long Seek(long offset, SeekOrigin origin): устанавливает позицию в потоке со смещением на количество байт, указанных в параметре offset.
        private static void CompressThreadFunction(FileInfo fi, string archivePath, long startReadOffset, long startWriteOffset, int readOffset, int writeOffset)
        {
            try
            {
                threadCount++;
                using (FileStream inFile = fi.OpenRead())
                {
                    inFile.Seek(startReadOffset, SeekOrigin.Begin);             // offset for thread from begin of read file
                    using (FileStream outFile = File.OpenWrite(archivePath))    // open created in main function archive file for write
                    {
                        outFile.Seek(startWriteOffset, SeekOrigin.Begin);       // offset for thread from begin of write file
                        using (GZipStream Compress = new GZipStream(outFile, CompressionMode.Compress))
                        {
                            // Пока можно читать файл
                            while (inFile.Position < inFile.Length)
                            {
                                // Читаем в буффер
                                // Сжимаем
                                // Записываем в итоговый файл
                                // Смещаем позицию в потоках чтения и записи
                                // Повторяем, пока читаемый файл не закончится

                                byte[] buffer = new byte[bufferSize];
                                inFile.Read(buffer, 0, buffer.Length);
                                Compress.Write(buffer, writeOffset, buffer.Length);
                                var compressedSize = outFile.Position;
                                //inFile.CopyTo(Compress, bufferSize);
                                // Compressed - change offset to next
                                inFile.Seek(bufferSize, SeekOrigin.Current);         // offset for thread from begin of read file
                                outFile.Seek(startWriteOffset, SeekOrigin.Current);  // offset for thread from begin of write file
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error: {0}", ex.Message);
            }
            finally
            {
                threadCount--;
            }
            /*
            using (var memory = new MemoryStream())
            {
                using
                    (var gZipStream = new GZipStream(memory, CompressionMode.Compress, true))
                {
                    //gZipStream.BaseStream.Position//Write(uncompressedBytes, 0, uncompressedBytes.Length);
                }
                return memory.ToArray();
            }
             */
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
