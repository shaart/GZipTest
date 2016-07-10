using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;                // FileInfo

namespace GZipTest
{
    /// <summary>
    /// Struct for read threads
    /// </summary>
    struct ReaderParameters
    {
        public int tid;
        public FileInfo originalFile;
        public long startReadOffset;
        public int consoleCursorTop;
        public bool showMessagesInConsole;

        /// <summary>
        /// Constructor of Struct for read threads
        /// </summary>
        /// <param name="tid">Local id of thread (in my thread pool)</param>
        /// <param name="originalFile">FileInfo of original file</param>
        /// <param name="startReadOffset">Start offset in stream for this thread</param>
        /// <param name="consoleCursorTop">Row in console for this thread info</param>
        /// <param name="showMessagesInConsole">Should the output messages to the console</param>
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
    /// <summary>
    /// Struct for write threads
    /// </summary>
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
}
