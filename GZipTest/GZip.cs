using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;                        // File, FileInfo, FileStream
using System.IO.Compression;            // GZipStream

namespace GZipTest
{
    class GZip
    {
        /// <summary>
        /// Buffer for GZip: 64 MB
        /// </summary>
        const int bufferSize = 64 * 1024;

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
                        // Create the compressed file.
                        using (FileStream outFile =
                                    File.Create(archivePath))
                        {
                            using (GZipStream Compress = new GZipStream(outFile, CompressionMode.Compress))
                            {
                                // Copy the source file into the compression stream.
                                inFile.CopyTo(Compress);

                                //Console.WriteLine("Compressed {0} from {1} to {2} bytes.",
                                //    fi.Name, fi.Length.ToString(), outFile.Length.ToString());
                                return outFile.Length;
                            }
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

        public static void Decompress(FileInfo archive, string file)
        {
            try
            {
                // Get the stream of the source file.
                using (FileStream inFile = archive.OpenRead())
                {
                    // Get original file extension, for example
                    // "doc" from report.doc.gz.
                    string curFile = archive.FullName;
                    string origName = curFile.Remove(curFile.Length -
                            archive.Extension.Length);

                    //Create the decompressed file.
                    using (FileStream outFile = File.Create(origName))
                    {
                        using (GZipStream Decompress = new GZipStream(inFile,
                                CompressionMode.Decompress))
                        {
                            // Copy the decompression stream 
                            // into the output file.
                            Decompress.CopyTo(outFile);

                            Console.WriteLine("Decompressed: {0}", archive.Name);

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
